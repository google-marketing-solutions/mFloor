# Disclaimer

**This is not an official Google product.**

    Copyright Google LLC. Supported by Google LLC and/or its affiliate(s). This solution, including any related sample code or data, is made available on an “as is,” “as available,” and “with all faults” basis, solely for illustrative purposes, and without warranty or representation of any kind. This solution is experimental, unsupported and provided solely for your convenience. Your use of it is subject to your agreements with Google, as applicable, and may constitute a beta feature as defined under those agreements.  To the extent that you make any data available to Google in connection with your use of the solution, you represent and warrant that you have all necessary and appropriate rights, consents and permissions to permit Google to use and process that data.  By using any portion of this solution, you acknowledge, assume and accept all risks, known and unknown, associated with its usage and any processing of data by Google, including with respect to your deployment of any portion of this solution in your systems, or usage in connection with your business, if at all. With respect to the entrustment of personal information to Google, you will verify that the established system is sufficient by checking Google's privacy policy and other public information, and you agree that no further information will be provided by Google.

# Multi-floor by Waterfall Demand Pressure

This is a Unity sample project of the "Multi-floor by Waterfall Demand Pressure" solution.

The solution automatically applies a user-level floor price to the RTB layer for each ad request based on historical waterfall demand pressure. Please refer to the slides for further explanation of its concept.

## `Assets/Scripts/AdMultiFloorManager.cs`

The `AdMultiFloorManager` class that manages recording, storing, and calculating historical waterfall demand pressure. This class is implemented as a singleton, you can call its methods from `AdMultiFloorManager.Instance`.

### Config how many days of historical data to look at

It's important to choose a reasonable date range when calculating historical waterfall demand pressure. For example, if most users use your app for multiple days (e.g. 30+ days), you might want to use last 7 days' data. If you are making a hyper-casual game, last 1 day's data may be a better choice.

You can change the setting in `AdMultiFloorManager.Performance._daysToKeep`.

### `void RegisterCandidates(string parentAdUnitId, IList<Candidate> candidates)`

Registers candidate ad units for the parent ad unit you want to enable multi-floor.
It's important to call this method **before** you make ad requests.

```c#
AdMultiFloorManager.Instance.RegisterCandidates(_parentAdUnitId,
        new AdMultiFloorManager.Candidate[]
        {
            new() { cpm = 1.0, adUnitId = "ca-app-pub-3940256099942544/1111111111" },
            new() { cpm = 2.0, adUnitId = "ca-app-pub-3940256099942544/2222222222" },
            new() { cpm = 3.0, adUnitId = "ca-app-pub-3940256099942544/3333333333" },
            new() { cpm = 4.0, adUnitId = "ca-app-pub-3940256099942544/4444444444" },
        });
```

### `Candidate? GetCandidate(string parentAdUnitId)`

Gets the candidate ad unit to use for next ad request. Returns null when no candidate is available.
This method calculates a referential CPM from historical records and finds the candidate with the highest CPM which is lower than referential CPM.
Make sure to pass **parent** ad unit id as the first parameter.

```c#
var candidate = AdMultiFloorManager.Instance.GetCandidate(_parentAdUnitId);
var adUnitId = candidate.HasValue ? candidate.Value.adUnitId : _parentAdUnitId;

RewardedAd.Load(adUnitId, adRequest, (RewardedAd ad, LoadAdError error) =>
{
    // ...
});
```

### `void RecordAdPaid(string parentAdUnitId, AdValue adValue)`

Records ad paid value. Call this inside GMA SDK's `OnAdPaid` callback.
Make sure to pass **parent** ad unit id as the first parameter.

```c#
ad.OnAdPaid += adValue =>
{
    AdMultiFloorManager.Instance.RecordAdPaid(_parentAdUnitId, adValue);
};
```

### `void RecordNoFill(string parentAdUnitId)`

Records no-fill. Call this inside `RewardedAd.Load`'s callback when ad request returns no-fill.
Make sure to pass **parent** ad unit id as the first parameter.

```c#
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
            AdMultiFloorManager.Instance.RecordNoFill(_parentAdUnitId);
        }
        return;
    }

    // ...
});
```

### Debugging methods

There are 3 methods to help you with debugging: `double GetReferentialCpm(string parentAdUnitId)`, `string GetDebugInfo()`, `void ClearStorage()`. Feel free to delete them from your production environment.

## `Assets/Scripts/AdController.cs`

This is a controller class demonstrating how to use `AdMultiFloorManager`.
Please be aware that it contains some debug methods (`ManualRecordNoFIll`, `ManualRecordAdPaid`, `ResetRecords`, etc.) that you won't need in your production environment.
