namespace Ms2.Net;

/// <summary>
/// CMsTime's time zone (commonfiles/mstime.cpp): the zone the server gives the client (CMsTime::SetTimeZone, from
/// CNetworkService::OnTimeSyncResponse kinds 0 and 1, or a result-7 LoginResult). Dates the client shows come from
/// CMsTime::GetLocalTm: gmtime of the time minus CalcZoneOffset (the zone seconds minus the daylight hours). The zone is
/// the seconds west of UTC (KST -32400); until the server sends one it is 0, so dates are UTC.
/// </summary>
public static class MsTime {
    public static bool ZoneSet { get; private set; }
    public static int ZoneSeconds { get; private set; }
    public static int DstHours { get; private set; }

    /// <summary>CMsTime::SetTimeZone(zoneSeconds, dstHours).</summary>
    public static void SetTimeZone(int zoneSeconds, int dstHours) {
        ZoneSet = true;
        ZoneSeconds = zoneSeconds;
        DstHours = dstHours;
    }

    /// <summary>CMsTime::CalcZoneOffset: zone seconds - dst hours * 3600.</summary>
    public static long CalcZoneOffset => ZoneSeconds - DstHours * 3600L;

    /// <summary>CMsTime::GetLocalTm of a Unix time: the calendar time the client shows for it.</summary>
    public static DateTime LocalTm(long unixSeconds) => DateTimeOffset.FromUnixTimeSeconds(unixSeconds - CalcZoneOffset).UtcDateTime;
}
