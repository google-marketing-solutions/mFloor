using GoogleMobileAds.Api;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AdController : MonoBehaviour
{
#if UNITY_ANDROID
    private readonly string _parentAdUnitId = "ca-app-pub-3940256099942544/5224354917";
#elif UNITY_IPHONE
    private readonly string _parentAdUnitId = "ca-app-pub-3940256099942544/1712485313";
#else
    private readonly string _parentAdUnitId = "unused";
#endif

    private bool _ready = false;

    private RewardedAd _rewardedAd;

    [SerializeField]
    private ScrollRect _consoleScrollRect;

    [SerializeField]
    private TextMeshProUGUI _consoleTMP;

    public void Start()
    {
        AdDynamicFloorManager.Instance.RegisterCandidates(_parentAdUnitId,
                new AdDynamicFloorManager.Candidate[]
                {
                    new() { cpm = 1.0, adUnitId = "ca-app-pub-3940256099942544/1111111111" },
                    new() { cpm = 2.0, adUnitId = "ca-app-pub-3940256099942544/2222222222" },
                    new() { cpm = 3.0, adUnitId = "ca-app-pub-3940256099942544/3333333333" },
                    new() { cpm = 4.0, adUnitId = "ca-app-pub-3940256099942544/4444444444" },
                });

        MobileAds.Initialize(initStatus =>
        {
            var map = initStatus.getAdapterStatusMap();
            foreach (var pair in map)
            {
                string className = pair.Key;
                AdapterStatus status = pair.Value;
                switch (status.InitializationState)
                {
                    case AdapterState.NotReady:
                        LogToConsole("Adapter: {0} not ready.", className);
                        break;
                    case AdapterState.Ready:
                        LogToConsole("Adapter: {0} is initialized.", className);
                        break;
                }
            }

            LogToConsole("MobileAds initialization finished.");
            _ready = true;
        });
    }

    public void LoadRewardedAd()
    {
        if (!_ready)
        {
            LogToConsole("MobileAds initialization not finished.");
            return;
        }

        if (_rewardedAd != null)
        {
            _rewardedAd.Destroy();
            _rewardedAd = null;
        }

        var adRequest = new AdRequest();

        var candidate = AdDynamicFloorManager.Instance.GetCandidate(_parentAdUnitId);
        var adUnitId = candidate.HasValue ? candidate.Value.adUnitId : _parentAdUnitId;

        LogToConsole(
            "Referential CPM: ${0}\nCandidate: {1}\n\nLoad ad unit: {2}",
            AdDynamicFloorManager.Instance.GetReferentialCpm(_parentAdUnitId),
            candidate.HasValue
                    ? string.Format("\n\tCPM: ${0}\n\tad unit: {1}",
                            candidate.Value.cpm, candidate.Value.adUnitId)
                    : "None",
            adUnitId
        );

        RewardedAd.Load(adUnitId, adRequest, (RewardedAd ad, LoadAdError error) =>
        {
            if (error != null || ad == null)
            {
                var errorCode = error.GetCode();
                if (
#if UNITY_ANDROID
                    // https://developers.google.com/android/reference/com/google/android/gms/ads/AdRequest#constants
                    errorCode == 3 || errorCode == 9
#elif UNITY_IPHONE
                    // https://developers.google.com/admob/ios/api/reference/Enums/GADErrorCode
                    errorCode == 1 || errorCode == 9
#else
                    false
#endif
                )
                {
                    AdDynamicFloorManager.Instance.RecordNoFill(_parentAdUnitId);
                }

                LogToConsole("Rewarded ad failed to load an ad with error: {0}", error);
                return;
            }

            _rewardedAd = ad;
            RegisterEventHandlers(_rewardedAd);

            LogToConsole("Rewarded ad loaded.");
        });
    }

    public void ShowRewardedAd()
    {
        if (_rewardedAd == null || !_rewardedAd.CanShowAd())
        {
            LogToConsole("Rewarded ad is not loaded.");
            return;
        }

        _rewardedAd.Show(reward =>
        {
            LogToConsole("Rewarded ad rewarded the user.");
        });
    }

    public void ManualRecordNoFIll()
    {
        AdDynamicFloorManager.Instance.RecordNoFill(_parentAdUnitId);
        LogToConsole("Recorded no fill.");
    }

    public void ManualRecordAdPaid(float usdValue)
    {
        AdDynamicFloorManager.Instance.RecordAdPaid(_parentAdUnitId, new AdValue
        {
            Precision = AdValue.PrecisionType.Estimated,
            CurrencyCode = "USD",
            Value = (long)Mathf.Round(usdValue * 1000000),
        });
        LogToConsole("Recorded ad paid ${0}.", usdValue);
    }

    public void ResetRecords()
    {
        AdDynamicFloorManager.Instance.ClearStorage();
        LogToConsole("Cleared.");
    }

    public void PrintDebugInfo()
    {
        LogToConsole(AdDynamicFloorManager.Instance.GetDebugInfo());
    }

    public void ClearConsole()
    {
        _consoleTMP.SetText("");
    }

    private void LogToConsole(string format, params object[] args)
    {
        LogToConsole(string.Format(format, args));
    }

    private void LogToConsole(string message)
    {
        _consoleTMP.SetText(
            message +
            "\n------------------\n" +
            _consoleTMP.text
        );

        _consoleScrollRect.StopMovement();
        _consoleScrollRect.normalizedPosition = new Vector2(0, 1);
    }

    private void RegisterEventHandlers(RewardedAd ad)
    {
        ad.OnAdPaid += adValue =>
        {
            LogToConsole("Rewarded ad paid {0} {1}.", adValue.Value, adValue.CurrencyCode);

            AdDynamicFloorManager.Instance.RecordAdPaid(_parentAdUnitId, adValue);
        };

        ad.OnAdFullScreenContentClosed += () =>
        {
            ad.Destroy();
        };

        ad.OnAdFullScreenContentFailed += error =>
        {
            LogToConsole("Rewarded ad failed to open full screen content with error: {0}",
                    error);

            ad.Destroy();
        };
    }
}
