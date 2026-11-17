using System;
using UnityEngine;

public static class ComfortSettings
{
    public const float HeightMin = 0.3f, HeightMax = 2.5f;
    public const float DistanceMin = 0.6f, DistanceMax = 2.5f;
    public const float SizeMin = 0.7f, SizeMax = 1.8f;

    const string KeyReduce = "comfort_reduce_motion";
    const string KeyHeight = "comfort_model_height";
    const string KeyDistance = "comfort_hint_distance";
    const string KeySize = "comfort_model_size";

    public static event Action Changed;

    static bool loaded;
    static bool reduceMotion;
    static float modelHeight = 1f, hintDistance = 1f, modelSize = 1f;

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        reduceMotion = PlayerPrefs.GetInt(KeyReduce, 0) == 1;
        modelHeight = PlayerPrefs.GetFloat(KeyHeight, 1f);
        hintDistance = PlayerPrefs.GetFloat(KeyDistance, 1f);
        modelSize = PlayerPrefs.GetFloat(KeySize, 1f);
    }

    public static bool ReduceMotion
    {
        get { Load(); return reduceMotion; }
        set { Load(); if (reduceMotion == value) return; reduceMotion = value; PlayerPrefs.SetInt(KeyReduce, value ? 1 : 0); Notify(); }
    }

    public static float ModelHeight
    {
        get { Load(); return modelHeight; }
        set { Load(); value = Mathf.Clamp(value, HeightMin, HeightMax); if (Mathf.Approximately(modelHeight, value)) return; modelHeight = value; PlayerPrefs.SetFloat(KeyHeight, value); Notify(); }
    }

    public static float HintDistance
    {
        get { Load(); return hintDistance; }
        set { Load(); value = Mathf.Clamp(value, DistanceMin, DistanceMax); if (Mathf.Approximately(hintDistance, value)) return; hintDistance = value; PlayerPrefs.SetFloat(KeyDistance, value); Notify(); }
    }

    public static float ModelSize
    {
        get { Load(); return modelSize; }
        set { Load(); value = Mathf.Clamp(value, SizeMin, SizeMax); if (Mathf.Approximately(modelSize, value)) return; modelSize = value; PlayerPrefs.SetFloat(KeySize, value); Notify(); }
    }

    /// <summary>Kembalikan semua ke nilai awal.</summary>
    public static void ResetAll()
    {
        Load();
        reduceMotion = false;
        modelHeight = hintDistance = modelSize = 1f;
        PlayerPrefs.SetInt(KeyReduce, 0);
        PlayerPrefs.SetFloat(KeyHeight, 1f);
        PlayerPrefs.SetFloat(KeyDistance, 1f);
        PlayerPrefs.SetFloat(KeySize, 1f);
        Notify();
    }

    static void Notify()
    {
        PlayerPrefs.Save();
        Changed?.Invoke();
    }
}
