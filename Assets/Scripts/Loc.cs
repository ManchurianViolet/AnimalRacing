using System;
using System.Collections.Generic;
using UnityEngine;

public enum GameLanguage { Korean = 0, English = 1, Japanese = 2 }

/// <summary>
/// [로컬라이제이션] 문자열 단일 관문 (static).
///
/// 데이터 흐름: Assets/Localization/strings.csv (유일한 편집 지점, UTF-8 BOM)
///   → 메뉴 [Tools > 짜고치는레이스 > 로컬라이제이션 CSV → 코드 굽기] (LocCsvTool)
///   → Assets/Scripts/Generated/LocData.g.cs (자동 생성 — 직접 수정 금지)
///
/// SO/Resources 대신 코드 생성을 쓰는 이유: static 컨텍스트에서 로드 타이밍 걱정 없이 즉시 조회,
/// Resources 규칙(§2 — Photon 스폰 대상만) 위반 없음, 에디트 모드에서도 동작.
///
/// 언어 값의 저장은 SettingsStore.Language 담당 (옵션 값 단일 출처 규칙),
/// 여기는 조회와 전환 이벤트만. 번역이 빈 칸이면 한국어(원문)로 폴백.
/// </summary>
public static class Loc
{
    /// <summary>언어가 바뀌는 순간 발행 — 화면에 이미 그려진 텍스트를 가진 UI가 구독해 다시 그린다.</summary>
    public static event Action OnLanguageChanged;

    // 없는 키 경고는 키당 1회만 (매 프레임 호출부에서 로그 폭탄 방지)
    private static readonly HashSet<string> warned = new();

    public static GameLanguage Language => SettingsStore.Language;

    /// <summary>키 → 현재 언어 문자열. 번역 빈 칸 = 한국어 폴백, 키 자체가 없으면 키를 그대로 반환.</summary>
    public static string Get(string key)
    {
        if (LocData.Table.TryGetValue(key, out var arr))
        {
            int i = (int)Language;
            if (i < arr.Length && !string.IsNullOrEmpty(arr[i])) return arr[i];
            if (arr.Length > 0 && !string.IsNullOrEmpty(arr[0])) return arr[0];
        }
        if (warned.Add(key)) Debug.LogWarning($"[Loc] 없는 키: {key}");
        return key;
    }

    /// <summary>특정 언어 고정 조회 — 무전기 LCD(전 언어 영문) 같은 특수 용도.</summary>
    public static string GetIn(GameLanguage lang, string key, string fallback = null)
    {
        if (LocData.Table.TryGetValue(key, out var arr))
        {
            int i = (int)lang;
            if (i < arr.Length && !string.IsNullOrEmpty(arr[i])) return arr[i];
            if (arr.Length > 0 && !string.IsNullOrEmpty(arr[0])) return arr[0];
        }
        return fallback ?? key;
    }

    /// <summary>키가 없을 수도 있는 조회 (지역 코드 등) — 없으면 경고 없이 fallback 반환.</summary>
    public static string Get(string key, string fallback)
    {
        if (LocData.Table.TryGetValue(key, out var arr))
        {
            int i = (int)Language;
            if (i < arr.Length && !string.IsNullOrEmpty(arr[i])) return arr[i];
            if (arr.Length > 0 && !string.IsNullOrEmpty(arr[0])) return arr[0];
        }
        return fallback;
    }

    /// <summary>서식 키 조회 + 즉시 조립. 서식 불일치는 삼켜지지 않고 에러 로그로 드러난다.</summary>
    public static string Format(string key, params object[] args)
    {
        string fmt = Get(key);
        try { return string.Format(fmt, args); }
        catch (FormatException)
        {
            Debug.LogError($"[Loc] 서식 불일치: {key} = \"{fmt}\" (인자 {args.Length}개)");
            return fmt;
        }
    }

    /// <summary>SettingsStore.Language 세터가 호출 — 직접 부르지 말 것.</summary>
    public static void NotifyLanguageChanged() => OnLanguageChanged?.Invoke();

    /// <summary>설정 UI용 — 언어의 자기 표기(항상 그 언어 문자로, 번역하지 않는 게 관례).</summary>
    public static string NativeName(GameLanguage lang) => lang switch
    {
        GameLanguage.Korean => "한국어",
        GameLanguage.English => "English",
        GameLanguage.Japanese => "日本語",
        _ => lang.ToString()
    };

    public const int LanguageCount = 3;
}
