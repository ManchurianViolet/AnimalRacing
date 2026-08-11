#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// glTF 2.0(.gltf/.glb) 정적 메시를 유니티 네이티브 에셋(메시 .asset + 머티리얼 + 프리팹)으로 한 번 굽는다.
/// 런타임 로더(glTFast 등)를 프로젝트에 들이지 않으려고 만든 에디터 전용 일회성 변환기다.
///
/// 지원: 노드 계층(matrix/TRS) 적용, 서브메시(머티리얼별), POSITION/NORMAL/TEXCOORD_0/TANGENT/COLOR,
///       pbrMetallicRoughness + KHR_materials_pbrSpecularGlossiness(구식 확장), 외부·임베디드 텍스처.
/// 미지원: Draco 압축, 스킨/애니메이션, 스파스 액세서, 모프 타깃 (만나면 경고 후 중단·건너뜀).
/// </summary>
public static class GltfBaker
{
    // glTF는 오른손 좌표계(+Y up, -Z forward), 유니티는 왼손(+Z forward) — z를 뒤집고 삼각형 감기를 반대로 한다
    private const string MenuPath = "Tools/짜고치는레이스/glTF 굽기 (선택한 .glb·.gltf)";

    [MenuItem(MenuPath, true)]
    private static bool ValidateBake()
    {
        string path = SelectedPath();
        return path != null;
    }

    [MenuItem(MenuPath)]
    private static void Bake()
    {
        string path = SelectedPath();
        if (path == null)
        {
            EditorUtility.DisplayDialog("glTF 굽기", "프로젝트 창에서 .glb 또는 .gltf 파일을 선택하고 실행해줘.", "확인");
            return;
        }

        try
        {
            BakeFile(path);
        }
        catch (Exception e)
        {
            Debug.LogError($"[glTF 굽기] 실패: {e.Message}\n{e.StackTrace}");
            EditorUtility.DisplayDialog("glTF 굽기", $"실패했어:\n{e.Message}", "확인");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private static string SelectedPath()
    {
        foreach (var obj in Selection.objects)
        {
            string p = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(p)) continue;
            string ext = Path.GetExtension(p).ToLowerInvariant();
            if (ext == ".glb" || ext == ".gltf") return p;
        }
        return null;
    }

    // ────────────────────────────────────────────────────────────── 본체

    private static void BakeFile(string assetPath)
    {
        string dir = Path.GetDirectoryName(assetPath).Replace('\\', '/');
        string baseName = Path.GetFileNameWithoutExtension(assetPath);
        bool isGlb = Path.GetExtension(assetPath).ToLowerInvariant() == ".glb";

        JObject root;
        byte[] glbBin = null;

        if (isGlb) root = ParseGlb(File.ReadAllBytes(assetPath), out glbBin);
        else root = JObject.Parse(File.ReadAllText(assetPath));

        // 다룰 수 없는 확장은 조용히 이상한 결과를 내지 말고 딱 잘라 중단한다
        var required = root["extensionsRequired"] as JArray;
        if (required != null)
        {
            foreach (var r in required)
            {
                string ext = r.ToString();
                if (ext.Contains("draco") || ext.Contains("meshopt"))
                    throw new Exception($"압축 확장 '{ext}'은 이 스크립트가 못 푼다. Sketchfab에서 비압축(.glb) 버전을 다시 받거나 glTFast를 써야 해.");
            }
        }

        // 버퍼 로드 (외부 .bin / data: base64 / glb 내장)
        var buffers = LoadBuffers(root, dir, glbBin);
        var views = root["bufferViews"] as JArray ?? new JArray();
        var accessors = root["accessors"] as JArray ?? new JArray();
        var meshes = root["meshes"] as JArray ?? new JArray();
        var nodes = root["nodes"] as JArray ?? new JArray();

        // 머티리얼별로 정점을 모은다 (하나의 메시 + 서브메시 N개로 굽는다)
        var groups = new Dictionary<int, Group>();

        // 씬 루트부터 노드 계층을 훑으며 월드 행렬을 누적
        var sceneIndex = root["scene"]?.Value<int>() ?? 0;
        var scenes = root["scenes"] as JArray;
        var rootNodes = (scenes != null && scenes.Count > sceneIndex)
            ? scenes[sceneIndex]["nodes"] as JArray
            : null;

        if (rootNodes == null)
        {
            // 씬 정의가 없으면 전 노드를 루트로 간주
            rootNodes = new JArray();
            for (int i = 0; i < nodes.Count; i++) rootNodes.Add(i);
        }

        foreach (var n in rootNodes)
            WalkNode(n.Value<int>(), Matrix4x4.identity, nodes, meshes, accessors, views, buffers, groups);

        if (groups.Count == 0) throw new Exception("구울 삼각형 메시를 하나도 못 찾았다.");

        // ── 메시 조립
        var mesh = new Mesh { name = baseName };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uvs = new List<Vector2>();
        var cols = new List<Color>();
        var subIndices = new List<int[]>();
        var matIndices = new List<int>();

        bool anyNormal = false, anyUv = false, anyColor = false;
        foreach (var g in groups.Values)
        {
            anyNormal |= g.HasNormal; anyUv |= g.HasUv; anyColor |= g.HasColor;
        }

        foreach (var kv in groups)
        {
            var g = kv.Value;
            int offset = verts.Count;
            verts.AddRange(g.Verts);
            // 일부 프리미티브만 속성이 있는 경우를 대비해 빈 자리를 기본값으로 메운다
            if (anyNormal) { if (g.HasNormal) norms.AddRange(g.Norms); else for (int i = 0; i < g.Verts.Count; i++) norms.Add(Vector3.up); }
            if (anyUv) { if (g.HasUv) uvs.AddRange(g.Uvs); else for (int i = 0; i < g.Verts.Count; i++) uvs.Add(Vector2.zero); }
            if (anyColor) { if (g.HasColor) cols.AddRange(g.Cols); else for (int i = 0; i < g.Verts.Count; i++) cols.Add(Color.white); }

            var idx = new int[g.Indices.Count];
            for (int i = 0; i < idx.Length; i++) idx[i] = g.Indices[i] + offset;
            subIndices.Add(idx);
            matIndices.Add(kv.Key);
        }

        mesh.SetVertices(verts);
        if (anyNormal) mesh.SetNormals(norms);
        if (anyUv) mesh.SetUVs(0, uvs);
        if (anyColor) mesh.SetColors(cols);
        mesh.subMeshCount = subIndices.Count;
        for (int i = 0; i < subIndices.Count; i++) mesh.SetTriangles(subIndices[i], i, true);

        if (!anyNormal) mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        mesh.Optimize();

        // ── 출력 폴더
        string outDir = $"{dir}/{baseName}_Baked";
        if (!AssetDatabase.IsValidFolder(outDir)) AssetDatabase.CreateFolder(dir, $"{baseName}_Baked");

        string meshPath = $"{outDir}/{baseName}.asset";
        AssetDatabase.DeleteAsset(meshPath);
        AssetDatabase.CreateAsset(mesh, meshPath);

        // ── 머티리얼
        var gltfMats = root["materials"] as JArray;
        var images = root["images"] as JArray;
        var textures = root["textures"] as JArray;
        var unityMats = new Material[matIndices.Count];

        for (int i = 0; i < matIndices.Count; i++)
        {
            int mi = matIndices[i];
            JObject gm = (gltfMats != null && mi >= 0 && mi < gltfMats.Count) ? gltfMats[mi] as JObject : null;
            string mname = gm?["name"]?.ToString() ?? $"Material_{mi}";
            unityMats[i] = BuildMaterial(gm, mname, outDir, dir, images, textures, views, buffers);
        }

        // ── 프리팹
        var go = new GameObject(baseName);
        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterials = unityMats;

        string prefabPath = $"{outDir}/{baseName}.prefab";
        AssetDatabase.DeleteAsset(prefabPath);
        PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
        UnityEngine.Object.DestroyImmediate(go);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var b = mesh.bounds;
        Debug.Log($"[glTF 굽기] 완료 — {prefabPath}\n" +
                  $"  정점 {verts.Count} / 삼각형 {CountTris(subIndices)} / 서브메시 {subIndices.Count}\n" +
                  $"  로컬 크기 {b.size.x:F3} × {b.size.y:F3} × {b.size.z:F3} (중심 {b.center.x:F3}, {b.center.y:F3}, {b.center.z:F3})\n" +
                  $"  가장 긴 축 = {LongestAxis(b.size)}", AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));

        Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
    }

    private static int CountTris(List<int[]> subs)
    {
        int n = 0;
        foreach (var s in subs) n += s.Length / 3;
        return n;
    }

    private static string LongestAxis(Vector3 size)
    {
        if (size.x >= size.y && size.x >= size.z) return $"X ({size.x:F3})";
        if (size.y >= size.z) return $"Y ({size.y:F3})";
        return $"Z ({size.z:F3})";
    }

    // ────────────────────────────────────────────────────────────── 노드 워크

    private class Group
    {
        public readonly List<Vector3> Verts = new List<Vector3>();
        public readonly List<Vector3> Norms = new List<Vector3>();
        public readonly List<Vector2> Uvs = new List<Vector2>();
        public readonly List<Color> Cols = new List<Color>();
        public readonly List<int> Indices = new List<int>();
        public bool HasNormal, HasUv, HasColor;
    }

    private static void WalkNode(int nodeIndex, Matrix4x4 parent, JArray nodes, JArray meshes,
        JArray accessors, JArray views, List<byte[]> buffers, Dictionary<int, Group> groups)
    {
        if (nodeIndex < 0 || nodeIndex >= nodes.Count) return;
        var node = nodes[nodeIndex] as JObject;
        Matrix4x4 local = NodeMatrix(node);
        Matrix4x4 world = parent * local;

        var meshRef = node["mesh"];
        if (meshRef != null)
        {
            int mi = meshRef.Value<int>();
            if (mi >= 0 && mi < meshes.Count)
                AppendMesh(meshes[mi] as JObject, world, accessors, views, buffers, groups);
        }

        var children = node["children"] as JArray;
        if (children != null)
            foreach (var c in children)
                WalkNode(c.Value<int>(), world, nodes, meshes, accessors, views, buffers, groups);
    }

    private static Matrix4x4 NodeMatrix(JObject node)
    {
        var m = node["matrix"] as JArray;
        if (m != null && m.Count == 16)
        {
            // glTF matrix는 열 우선(column-major) 16개 — Matrix4x4.SetColumn 순서와 맞춘다
            var mat = new Matrix4x4();
            for (int c = 0; c < 4; c++)
                mat.SetColumn(c, new Vector4(
                    m[c * 4 + 0].Value<float>(), m[c * 4 + 1].Value<float>(),
                    m[c * 4 + 2].Value<float>(), m[c * 4 + 3].Value<float>()));
            return mat;
        }

        Vector3 t = ReadVec3(node["translation"], Vector3.zero);
        Vector3 s = ReadVec3(node["scale"], Vector3.one);
        var rArr = node["rotation"] as JArray;
        Quaternion r = (rArr != null && rArr.Count == 4)
            ? new Quaternion(rArr[0].Value<float>(), rArr[1].Value<float>(), rArr[2].Value<float>(), rArr[3].Value<float>())
            : Quaternion.identity;
        return Matrix4x4.TRS(t, r, s);
    }

    private static Vector3 ReadVec3(JToken tok, Vector3 fallback)
    {
        var a = tok as JArray;
        if (a == null || a.Count < 3) return fallback;
        return new Vector3(a[0].Value<float>(), a[1].Value<float>(), a[2].Value<float>());
    }

    private static void AppendMesh(JObject mesh, Matrix4x4 world, JArray accessors, JArray views,
        List<byte[]> buffers, Dictionary<int, Group> groups)
    {
        var prims = mesh?["primitives"] as JArray;
        if (prims == null) return;

        // 비균등 스케일에서 법선이 찌그러지지 않게 역전치 행렬을 쓴다
        Matrix4x4 nrmMat = world.inverse.transpose;

        foreach (var pTok in prims)
        {
            var prim = pTok as JObject;
            int mode = prim["mode"]?.Value<int>() ?? 4;
            if (mode != 4)
            {
                Debug.LogWarning($"[glTF 굽기] 삼각형(mode 4)이 아닌 프리미티브(mode {mode})는 건너뜀");
                continue;
            }

            var attrs = prim["attributes"] as JObject;
            if (attrs?["POSITION"] == null) continue;

            var pos = ReadAccessorVec3(attrs["POSITION"].Value<int>(), accessors, views, buffers);
            var nrm = attrs["NORMAL"] != null ? ReadAccessorVec3(attrs["NORMAL"].Value<int>(), accessors, views, buffers) : null;
            var uv = attrs["TEXCOORD_0"] != null ? ReadAccessorVec2(attrs["TEXCOORD_0"].Value<int>(), accessors, views, buffers) : null;
            var col = attrs["COLOR_0"] != null ? ReadAccessorColor(attrs["COLOR_0"].Value<int>(), accessors, views, buffers) : null;

            int matIndex = prim["material"]?.Value<int>() ?? -1;
            if (!groups.TryGetValue(matIndex, out var g)) groups[matIndex] = g = new Group();

            int baseVert = g.Verts.Count;
            for (int i = 0; i < pos.Length; i++)
            {
                Vector3 p = world.MultiplyPoint3x4(pos[i]);
                g.Verts.Add(new Vector3(p.x, p.y, -p.z));   // 오른손 → 왼손
            }
            if (nrm != null)
            {
                g.HasNormal = true;
                for (int i = 0; i < nrm.Length; i++)
                {
                    Vector3 n = nrmMat.MultiplyVector(nrm[i]).normalized;
                    g.Norms.Add(new Vector3(n.x, n.y, -n.z));
                }
            }
            if (uv != null)
            {
                g.HasUv = true;
                // glTF UV 원점은 좌상단, 유니티는 좌하단
                for (int i = 0; i < uv.Length; i++) g.Uvs.Add(new Vector2(uv[i].x, 1f - uv[i].y));
            }
            if (col != null)
            {
                g.HasColor = true;
                g.Cols.AddRange(col);
            }

            // 인덱스 (없으면 순차 삼각형)
            int[] indices;
            if (prim["indices"] != null)
                indices = ReadAccessorInt(prim["indices"].Value<int>(), accessors, views, buffers);
            else
            {
                indices = new int[pos.Length];
                for (int i = 0; i < indices.Length; i++) indices[i] = i;
            }

            // z를 뒤집었으니 감기 방향도 뒤집어야 앞면이 유지된다
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                g.Indices.Add(baseVert + indices[i + 0]);
                g.Indices.Add(baseVert + indices[i + 2]);
                g.Indices.Add(baseVert + indices[i + 1]);
            }
        }
    }

    // ────────────────────────────────────────────────────────────── 액세서 읽기

    private static int ComponentSize(int t)
    {
        switch (t)
        {
            case 5120: case 5121: return 1;
            case 5122: case 5123: return 2;
            case 5125: case 5126: return 4;
            default: throw new Exception($"모르는 componentType {t}");
        }
    }

    private static int ComponentCount(string type)
    {
        switch (type)
        {
            case "SCALAR": return 1;
            case "VEC2": return 2;
            case "VEC3": return 3;
            case "VEC4": return 4;
            case "MAT4": return 16;
            default: throw new Exception($"모르는 accessor type {type}");
        }
    }

    /// <summary>액세서 하나를 float 배열로 정규화해 읽는다 (컴포넌트 단위 평탄화).</summary>
    private static float[] ReadFloats(int accessorIndex, JArray accessors, JArray views, List<byte[]> buffers, out int compCount)
    {
        var acc = accessors[accessorIndex] as JObject;
        if (acc["sparse"] != null) throw new Exception("스파스 액세서는 지원 안 함");

        string type = acc["type"].ToString();
        int ct = acc["componentType"].Value<int>();
        int count = acc["count"].Value<int>();
        bool normalized = acc["normalized"]?.Value<bool>() ?? false;
        compCount = ComponentCount(type);
        int cs = ComponentSize(ct);
        int elemSize = cs * compCount;

        var result = new float[count * compCount];

        if (acc["bufferView"] == null) return result;   // 스펙상 0으로 채운 것과 같다

        var view = views[acc["bufferView"].Value<int>()] as JObject;
        byte[] buf = buffers[view["buffer"].Value<int>()];
        int viewOffset = view["byteOffset"]?.Value<int>() ?? 0;
        int stride = view["byteStride"]?.Value<int>() ?? 0;
        if (stride == 0) stride = elemSize;
        int accOffset = acc["byteOffset"]?.Value<int>() ?? 0;
        int start = viewOffset + accOffset;

        for (int i = 0; i < count; i++)
        {
            int o = start + i * stride;
            for (int c = 0; c < compCount; c++)
            {
                int p = o + c * cs;
                float v;
                switch (ct)
                {
                    case 5126: v = BitConverter.ToSingle(buf, p); break;
                    case 5125: v = BitConverter.ToUInt32(buf, p); break;
                    case 5123: v = BitConverter.ToUInt16(buf, p); if (normalized) v /= 65535f; break;
                    case 5122: v = BitConverter.ToInt16(buf, p); if (normalized) v = Mathf.Max(v / 32767f, -1f); break;
                    case 5121: v = buf[p]; if (normalized) v /= 255f; break;
                    case 5120: v = (sbyte)buf[p]; if (normalized) v = Mathf.Max(v / 127f, -1f); break;
                    default: throw new Exception($"모르는 componentType {ct}");
                }
                result[i * compCount + c] = v;
            }
        }
        return result;
    }

    private static Vector3[] ReadAccessorVec3(int i, JArray a, JArray v, List<byte[]> b)
    {
        var f = ReadFloats(i, a, v, b, out int cc);
        var r = new Vector3[f.Length / cc];
        for (int k = 0; k < r.Length; k++) r[k] = new Vector3(f[k * cc], f[k * cc + 1], f[k * cc + 2]);
        return r;
    }

    private static Vector2[] ReadAccessorVec2(int i, JArray a, JArray v, List<byte[]> b)
    {
        var f = ReadFloats(i, a, v, b, out int cc);
        var r = new Vector2[f.Length / cc];
        for (int k = 0; k < r.Length; k++) r[k] = new Vector2(f[k * cc], f[k * cc + 1]);
        return r;
    }

    private static Color[] ReadAccessorColor(int i, JArray a, JArray v, List<byte[]> b)
    {
        var f = ReadFloats(i, a, v, b, out int cc);
        var r = new Color[f.Length / cc];
        for (int k = 0; k < r.Length; k++)
            r[k] = new Color(f[k * cc], f[k * cc + 1], f[k * cc + 2], cc >= 4 ? f[k * cc + 3] : 1f);
        return r;
    }

    private static int[] ReadAccessorInt(int i, JArray a, JArray v, List<byte[]> b)
    {
        var f = ReadFloats(i, a, v, b, out int cc);
        var r = new int[f.Length];
        for (int k = 0; k < f.Length; k++) r[k] = Mathf.RoundToInt(f[k]);
        return r;
    }

    // ────────────────────────────────────────────────────────────── 버퍼 / glb

    private static List<byte[]> LoadBuffers(JObject root, string dir, byte[] glbBin)
    {
        var list = new List<byte[]>();
        var buffers = root["buffers"] as JArray;
        if (buffers == null) return list;

        foreach (var bTok in buffers)
        {
            var b = bTok as JObject;
            string uri = b["uri"]?.ToString();
            if (string.IsNullOrEmpty(uri))
            {
                if (glbBin == null) throw new Exception("uri 없는 버퍼인데 glb 바이너리 청크가 없다");
                list.Add(glbBin);
            }
            else if (uri.StartsWith("data:"))
            {
                int comma = uri.IndexOf(',');
                list.Add(Convert.FromBase64String(uri.Substring(comma + 1)));
            }
            else
            {
                string p = $"{dir}/{Uri.UnescapeDataString(uri)}";
                if (!File.Exists(p)) throw new Exception($"버퍼 파일을 못 찾음: {p}");
                list.Add(File.ReadAllBytes(p));
            }
        }
        return list;
    }

    private static JObject ParseGlb(byte[] data, out byte[] bin)
    {
        bin = null;
        if (data.Length < 12) throw new Exception("glb가 너무 짧다");
        uint magic = BitConverter.ToUInt32(data, 0);
        if (magic != 0x46546C67) throw new Exception("glb 매직('glTF')이 아니다");

        JObject json = null;
        int p = 12;
        while (p + 8 <= data.Length)
        {
            int len = BitConverter.ToInt32(data, p);
            uint type = BitConverter.ToUInt32(data, p + 4);
            p += 8;
            if (p + len > data.Length) break;

            if (type == 0x4E4F534A)          // 'JSON'
            {
                json = JObject.Parse(System.Text.Encoding.UTF8.GetString(data, p, len));
            }
            else if (type == 0x004E4942)     // 'BIN'
            {
                bin = new byte[len];
                Array.Copy(data, p, bin, 0, len);
            }
            p += len;
            if (p % 4 != 0) p += 4 - (p % 4);
        }

        if (json == null) throw new Exception("glb에서 JSON 청크를 못 찾았다");
        return json;
    }

    // ────────────────────────────────────────────────────────────── 머티리얼 / 텍스처

    private static Material BuildMaterial(JObject gm, string name, string outDir, string srcDir,
        JArray images, JArray textures, JArray views, List<byte[]> buffers)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var mat = new Material(shader) { name = SanitizeName(name) };

        Color baseColor = Color.white;
        float smoothness = 0.5f, metallic = 0f;
        int texIndex = -1;

        var specGloss = gm?["extensions"]?["KHR_materials_pbrSpecularGlossiness"] as JObject;
        var pbr = gm?["pbrMetallicRoughness"] as JObject;

        if (specGloss != null)
        {
            // 구식 스펙큘러-글로시니스 확장 — URP Lit에 근사 대입한다 (디퓨즈 텍스처 + 글로시니스만 살림)
            baseColor = ReadColor(specGloss["diffuseFactor"], Color.white);
            smoothness = specGloss["glossinessFactor"]?.Value<float>() ?? 1f;
            metallic = 0f;
            texIndex = specGloss["diffuseTexture"]?["index"]?.Value<int>() ?? -1;
        }
        else if (pbr != null)
        {
            baseColor = ReadColor(pbr["baseColorFactor"], Color.white);
            float rough = pbr["roughnessFactor"]?.Value<float>() ?? 1f;
            smoothness = 1f - rough;
            metallic = pbr["metallicFactor"]?.Value<float>() ?? 1f;
            texIndex = pbr["baseColorTexture"]?["index"]?.Value<int>() ?? -1;
        }

        mat.SetColor("_BaseColor", baseColor);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Metallic", metallic);

        if (texIndex >= 0)
        {
            var tex = ResolveTexture(texIndex, outDir, srcDir, images, textures, views, buffers, false);
            if (tex != null) mat.SetTexture("_BaseMap", tex);
        }

        // 노멀맵 — 로우폴리일수록 이게 있고 없고의 차이가 크다
        var normalIdx = gm?["normalTexture"]?["index"];
        if (normalIdx != null)
        {
            var ntex = ResolveTexture(normalIdx.Value<int>(), outDir, srcDir, images, textures, views, buffers, true);
            if (ntex != null)
            {
                mat.SetTexture("_BumpMap", ntex);
                mat.EnableKeyword("_NORMALMAP");
                var scale = gm["normalTexture"]["scale"];
                if (scale != null) mat.SetFloat("_BumpScale", scale.Value<float>());
            }
        }

        // metallicRoughness는 일부러 건너뛴다 — glTF는 G=거칠기/B=금속인데 URP _MetallicGlossMap은
        // R=금속/A=매끄러움이라 채널이 어긋난다. 그대로 물리면 엉뚱하게 번들거린다.
        // 필요하면 채널을 재배치한 텍스처를 따로 구워야 하므로, 여기서는 상수값(_Metallic/_Smoothness)만 쓴다.
        if (pbr?["metallicRoughnessTexture"] != null)
            Debug.LogWarning($"[glTF 굽기] '{name}'에 metallicRoughness 텍스처가 있지만 채널 배치가 달라 건너뜀 " +
                             "— 금속/거칠기는 머티리얼 슬라이더로 맞추세요");

        string matPath = $"{outDir}/{mat.name}.mat";
        AssetDatabase.DeleteAsset(matPath);
        AssetDatabase.CreateAsset(mat, matPath);
        return AssetDatabase.LoadAssetAtPath<Material>(matPath);
    }

    private static Color ReadColor(JToken tok, Color fallback)
    {
        var a = tok as JArray;
        if (a == null || a.Count < 3) return fallback;
        return new Color(a[0].Value<float>(), a[1].Value<float>(), a[2].Value<float>(),
            a.Count >= 4 ? a[3].Value<float>() : 1f);
    }

    private static Texture2D ResolveTexture(int texIndex, string outDir, string srcDir,
        JArray images, JArray textures, JArray views, List<byte[]> buffers, bool asNormalMap)
    {
        if (textures == null || texIndex >= textures.Count) return null;
        var srcTok = textures[texIndex]["source"];
        if (srcTok == null || images == null) return null;

        int imgIndex = srcTok.Value<int>();
        if (imgIndex >= images.Count) return null;
        var img = images[imgIndex] as JObject;

        string uri = img["uri"]?.ToString();
        if (!string.IsNullOrEmpty(uri) && !uri.StartsWith("data:"))
        {
            // 외부 파일 — 이미 프로젝트에 있으니 그대로 참조
            string p = $"{srcDir}/{Uri.UnescapeDataString(uri)}";
            if (asNormalMap) MarkAsNormalMap(p);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
        }

        // 임베디드(glb bufferView 또는 data:) — 파일로 뽑아낸다
        byte[] bytes;
        if (!string.IsNullOrEmpty(uri))
        {
            int comma = uri.IndexOf(',');
            bytes = Convert.FromBase64String(uri.Substring(comma + 1));
        }
        else if (img["bufferView"] != null)
        {
            var view = views[img["bufferView"].Value<int>()] as JObject;
            byte[] buf = buffers[view["buffer"].Value<int>()];
            int off = view["byteOffset"]?.Value<int>() ?? 0;
            int len = view["byteLength"].Value<int>();
            bytes = new byte[len];
            Array.Copy(buf, off, bytes, 0, len);
        }
        else return null;

        string mime = img["mime"]?.ToString() ?? img["mimeType"]?.ToString() ?? "image/png";
        string ext = mime.Contains("jpeg") || mime.Contains("jpg") ? ".jpg" : ".png";
        string outPath = $"{outDir}/{SanitizeName(img["name"]?.ToString() ?? $"texture_{imgIndex}")}{ext}";

        File.WriteAllBytes(outPath, bytes);
        AssetDatabase.ImportAsset(outPath, ImportAssetOptions.ForceUpdate);
        if (asNormalMap) MarkAsNormalMap(outPath);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
    }

    /// <summary>
    /// 노멀맵은 임포터 타입을 바꿔줘야 한다 — 일반 텍스처로 두면 유니티가 sRGB로 해석해
    /// 굴곡이 뭉개지고 "노멀맵인데 왜 밋밋하지" 상태가 된다.
    /// </summary>
    private static void MarkAsNormalMap(string path)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null || ti.textureType == TextureImporterType.NormalMap) return;
        ti.textureType = TextureImporterType.NormalMap;
        ti.SaveAndReimport();
    }

    private static string SanitizeName(string s)
    {
        if (string.IsNullOrEmpty(s)) return "Unnamed";
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s;
    }
}
#endif
