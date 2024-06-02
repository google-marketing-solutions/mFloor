using System;
using System.Collections.Generic;
using System.Linq;
using GoogleMobileAds.Api;
using Newtonsoft.Json;
using UnityEngine;

public class AdDynamicFloorManager : MonoBehaviour
{
    public struct Candidate
    {
        [JsonProperty]
        internal double cpm;
        [JsonProperty]
        internal string adUnitId;
    }

    private class Performance
    {
        private class Entry
        {
            // This should be ad-paids + no-fills.
            public uint count;

            // Micro value to align with AdValue.Value.
            public long usdValue;
        }

        private const int _daysToKeep = 7;

        [JsonProperty]
        private readonly Dictionary<int, Entry> _entryByDate = new();

        private int GetDateInt(DateTime dateTime)
        {
            return int.Parse(dateTime.ToString("yyyyMMdd"));
        }

        internal void Record(AdValue adValue)
        {
            // Skip if currency isn't USD.
            if (adValue.CurrencyCode != "USD")
            {
                return;
            }

            var date = GetDateInt(DateTime.Now);

            if (_entryByDate.TryGetValue(date, out var entry))
            {
                entry.count++;
                entry.usdValue += adValue.Value;
            }
            else
            {
                entry = new Entry
                {
                    count = 1,
                    usdValue = adValue.Value,
                };
                _entryByDate.Add(date, entry);
            }
        }

        internal double GetReferentialCpm()
        {
            var minDate = GetDateInt(DateTime.Now) - _daysToKeep;

            uint totalCount = 0;
            double totalUsdValue = 0;

            foreach (var pair in _entryByDate)
            {
                if (pair.Key <= minDate)
                {
                    continue;
                }

                totalCount += pair.Value.count;
                totalUsdValue += pair.Value.usdValue;
            }

            return (totalUsdValue / 1000000) / totalCount * 1000;
        }

        internal void Clean()
        {
            var minDate = GetDateInt(DateTime.Now) - _daysToKeep;
            var entriesToRemove = _entryByDate
                    .Where(e => e.Key <= minDate).ToArray();
            foreach (var pair in entriesToRemove)
            {
                _entryByDate.Remove(pair.Key);
            }
        }
    }

    #region Singleton
    public static AdDynamicFloorManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<AdDynamicFloorManager>();

                if (_instance == null)
                {
                    var obj = new GameObject { name = typeof(AdDynamicFloorManager).Name };
                    _instance = obj.AddComponent<AdDynamicFloorManager>();
                }
            }

            return _instance;
        }
    }

    private static AdDynamicFloorManager _instance;

    private void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
            _instance.LoadFromStorage();
        }
        else
        {
            Destroy(gameObject);
        }
    }
    #endregion

    private const string _playerPrefKey = "AdDynamicFloorManager.performanceByAdUnitId";

    private readonly Dictionary<string, Performance> _performanceByAdUnitId = new();
    private bool _synced = false;

    private readonly Dictionary<string, IList<Candidate>> _candidatesByAdUnitId = new();

    public void RecordNoFill(string adUnitId)
    {
        Record(adUnitId, new AdValue
        {
            Precision = AdValue.PrecisionType.Estimated,
            CurrencyCode = "USD",
            Value = 0,
        });
    }

    public void RecordAdPaid(string adUnitId, AdValue adValue)
    {
        // Skip non-waterfall cases.
        if (adValue.Precision != AdValue.PrecisionType.Estimated &&
            adValue.Precision != AdValue.PrecisionType.PublisherProvided)
        {
            return;
        }

        Record(adUnitId, adValue);
    }

    public void RegisterCandidates(string adUnitId, IList<Candidate> candidates)
    {
        var sorted = new List<Candidate>(candidates);
        // Sort from high to low.
        sorted.Sort((a, b) => a.cpm > b.cpm ? -1 : 1);
        _candidatesByAdUnitId.Add(adUnitId, sorted);
    }

    public Candidate? GetCandidate(string adUnitId)
    {
        if (!_performanceByAdUnitId.TryGetValue(adUnitId, out var performance) ||
                !_candidatesByAdUnitId.TryGetValue(adUnitId, out var candidateList))
        {
            return null;
        }

        var referentialCpm = performance.GetReferentialCpm();

        foreach (var candidate in candidateList)
        {
            if (candidate.cpm < referentialCpm)
            {
                return candidate;
            }
        }

        return null;
    }

    private void Record(string adUnitId, AdValue adValue)
    {
        if (!_performanceByAdUnitId.TryGetValue(adUnitId, out var performance))
        {
            performance = new Performance();
            _performanceByAdUnitId.Add(adUnitId, performance);
        }

        performance.Record(adValue);

        _synced = false;
    }

    private void LoadFromStorage()
    {
        if (PlayerPrefs.HasKey(_playerPrefKey))
        {
            Dictionary<string, Performance> storage;

            try
            {
                string jsonStr = PlayerPrefs.GetString(_playerPrefKey);
                storage = JsonConvert.DeserializeObject<Dictionary<string, Performance>>(jsonStr);
            }
            catch (Exception)
            {
                Debug.LogErrorFormat("[AdDynamicFloorManager] Failed loading from storage.");
                return;
            }

            _performanceByAdUnitId.Clear();

            foreach (var pair in storage)
            {
                pair.Value.Clean();
                _performanceByAdUnitId.Add(pair.Key, pair.Value);
            }
        }
        else
        {
            _performanceByAdUnitId.Clear();
        }

        _synced = true;
    }

    private void SaveToStorage()
    {
        if (_synced)
        {
            return;
        }

        try
        {
            var jsonStr = JsonConvert.SerializeObject(_performanceByAdUnitId);
            PlayerPrefs.SetString(_playerPrefKey, jsonStr);
        }
        catch (Exception)
        {
            Debug.LogError("[AdDynamicFloorManager] Failed saving to storage.");
        }

        _synced = true;
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
        {
            SaveToStorage();
        }
    }

    private void OnApplicationPause(bool isPaused)
    {
        if (isPaused)
        {
            SaveToStorage();
        }
    }

    private void OnApplicationQuit()
    {
        SaveToStorage();
    }

    #region Debug
    public double GetReferentialCpm(string adUnitId)
    {
        if (!_performanceByAdUnitId.TryGetValue(adUnitId, out var performance))
        {
            return 0;
        }

        return performance.GetReferentialCpm();
    }

    public string GetDebugInfo()
    {
        return string.Format("_performanceByAdUnitId:\n{0}\n\n_candidatesByAdUnitId:\n{1}",
                JsonConvert.SerializeObject(_performanceByAdUnitId, Formatting.Indented),
                JsonConvert.SerializeObject(_candidatesByAdUnitId, Formatting.Indented)
        );
    }

    public void ClearStorage()
    {
        PlayerPrefs.DeleteKey(_playerPrefKey);
        LoadFromStorage();
    }
    #endregion
}
