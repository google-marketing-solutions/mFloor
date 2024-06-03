/*
 * Copyright 2024 Google LLC
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *      https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using GoogleMobileAds.Api;
using Newtonsoft.Json;
using UnityEngine;

public class AdMultiFloorManager : MonoBehaviour
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
            /// <summary>
            /// This should be ad-paids + no-fills.
            /// </summary>
            public uint count;

            /// <summary>
            /// Micro value to align with AdValue.Value.
            /// </summary>
            public long usdValue;
        }

        /// <summary>
        /// Days of entries to keep. Change this to best reflect your user lifecycle.
        /// </summary>
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

        /// <summary>
        /// Remove old entries.
        /// </summary>
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
    public static AdMultiFloorManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<AdMultiFloorManager>();

                if (_instance == null)
                {
                    var obj = new GameObject { name = typeof(AdMultiFloorManager).Name };
                    _instance = obj.AddComponent<AdMultiFloorManager>();
                }
            }

            return _instance;
        }
    }

    private static AdMultiFloorManager _instance;

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

    private const string _playerPrefKey = "AdMultiFloorManager.performanceByAdUnitId";

    private readonly Dictionary<string, Performance> _performanceByAdUnitId = new();
    private bool _synced = false;

    private readonly Dictionary<string, IList<Candidate>> _candidatesByAdUnitId = new();

    /// <summary>
    /// Records no-fill. Call this inside `RewardedAd.Load`'s callback when ad request returns no-fill.
    /// </summary>
    public void RecordNoFill(string parentAdUnitId)
    {
        Record(parentAdUnitId, new AdValue
        {
            Precision = AdValue.PrecisionType.Estimated,
            CurrencyCode = "USD",
            Value = 0,
        });
    }

    /// <summary>
    /// Records ad paid value. Call this inside GMA SDK's `OnAdPaid` callback.
    /// </summary>
    public void RecordAdPaid(string parentAdUnitId, AdValue adValue)
    {
        // Skip non-waterfall cases.
        if (adValue.Precision != AdValue.PrecisionType.Estimated &&
            adValue.Precision != AdValue.PrecisionType.PublisherProvided)
        {
            return;
        }

        Record(parentAdUnitId, adValue);
    }

    /// <summary>
    /// Registers candidate ad units for the parent ad unit you want to enable multi-floor.
    /// It's important to call this method **before** you make ad requests.
    /// </summary>
    public void RegisterCandidates(string parentAdUnitId, IList<Candidate> candidates)
    {
        var sorted = new List<Candidate>(candidates);
        // Sort from high to low.
        sorted.Sort((a, b) => a.cpm > b.cpm ? -1 : 1);
        _candidatesByAdUnitId.Add(parentAdUnitId, sorted);
    }

    /// <summary>
    /// Gets the candidate ad unit to use for next ad request.
    /// Returns null when no candidate is available.
    /// This method calculates a referential CPM from historical records and
    /// finds the candidate with the highest CPM which is lower than referential CPM.
    /// </summary>
    public Candidate? GetCandidate(string parentAdUnitId)
    {
        if (!_performanceByAdUnitId.TryGetValue(parentAdUnitId, out var performance) ||
                !_candidatesByAdUnitId.TryGetValue(parentAdUnitId, out var candidateList))
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

    private void Record(string parentAdUnitId, AdValue adValue)
    {
        if (!_performanceByAdUnitId.TryGetValue(parentAdUnitId, out var performance))
        {
            performance = new Performance();
            _performanceByAdUnitId.Add(parentAdUnitId, performance);
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
                Debug.LogErrorFormat("[AdMultiFloorManager] Failed loading from storage.");
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
            Debug.LogError("[AdMultiFloorManager] Failed saving to storage.");
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
    public double GetReferentialCpm(string parentAdUnitId)
    {
        if (!_performanceByAdUnitId.TryGetValue(parentAdUnitId, out var performance))
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
