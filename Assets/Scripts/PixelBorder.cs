using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
[ExecuteAlways]
public class PixelBorder : MonoBehaviour
{
    [Header("Colors")]
    public Color borderColor = Color.white;
    public Color fillColor = new Color(0.04f, 0.04f, 0.10f, 1f); // #0a0a1a

    [Header("Border")]
    public int borderWidth = 1;

    private Sprite generated;

    void OnEnable() { Apply(); }
    void OnValidate() { Apply(); }

    void Apply()
    {
        var img = GetComponent<Image>();
        if (img == null) return;
        if (borderWidth < 1) borderWidth = 1;

        // 9-slice¿ë ÃÖ¼Ò »çÀÌÁî: borderWidth*2 + 1 (Áß¾Ó stretch¿ë 1ÇÈ¼¿)
        int s = borderWidth * 2 + 1;
        Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;

        Color[] px = new Color[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                bool isBorder = x < borderWidth || x >= s - borderWidth
                             || y < borderWidth || y >= s - borderWidth;
                px[y * s + x] = isBorder ? borderColor : fillColor;
            }
        tex.SetPixels(px);
        tex.Apply();

        // º¯°æ ÈÄ
        if (generated != null) DestroyImmediate(generated);

        // Canvas referencePixelsPerUnit°ú ¸ÅÄªÇØ¼­ 1ÇÈ¼¿ = 1Äµ¹ö½ºÇÈ¼¿
        float ppu = 100f;
        var canvas = GetComponentInParent<Canvas>();
        if (canvas != null && canvas.rootCanvas != null && canvas.rootCanvas.referencePixelsPerUnit > 0)
            ppu = canvas.rootCanvas.referencePixelsPerUnit;

        generated = Sprite.Create(
            tex,
            new Rect(0, 0, s, s),
            new Vector2(0.5f, 0.5f),
            ppu, 0, SpriteMeshType.FullRect,
            new Vector4(borderWidth, borderWidth, borderWidth, borderWidth)
        );

        img.sprite = generated;
        img.type = Image.Type.Sliced;
        img.fillCenter = true;
        img.pixelsPerUnitMultiplier = 1f;
        img.color = Color.white;
    }
}