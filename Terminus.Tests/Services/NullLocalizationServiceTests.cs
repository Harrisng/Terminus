using FluentAssertions;
using Terminus.Core.Services;
using Xunit;

namespace Terminus.Core.Tests.Services;

/// <summary>
/// NullLocalizationService 合約測試。
/// 此實作為測試與未初始化環境的預設 ILocalizationService，
/// 必須穩定回傳 [key] 標記以利呼叫端識別 missing key。
/// </summary>
public class NullLocalizationServiceTests
{
    [Fact]
    public void Get_WithoutArgs_ReturnsBracketedKey()
    {
        var svc = new NullLocalizationService();

        var result = svc.Get("Warning_QuotaExceededTitle");

        result.Should().Be("[Warning_QuotaExceededTitle]");
    }

    [Fact]
    public void Get_WithArgs_ReturnsBracketedKeyFollowedArgs()
    {
        var svc = new NullLocalizationService();

        var result = svc.Get("Warning_QuotaExceededBody", 15.0, 30.0);

        result.Should().Be("[Warning_QuotaExceededBody]15,30");
    }

    [Fact]
    public void Get_WithSingleArg_ReturnsBracketedKeyFollowedSingleArg()
    {
        var svc = new NullLocalizationService();

        var result = svc.Get("Notification_PreWarningBody", "5 分鐘", "明日早課 09:00");

        result.Should().StartWith("[Notification_PreWarningBody]");
        result.Should().Contain("5 分鐘");
        result.Should().Contain("明日早課 09:00");
    }

    [Fact]
    public void Get_SameKey_SameResult_StableContract()
    {
        var svc = new NullLocalizationService();

        var first = svc.Get("Any_Key");
        var second = svc.Get("Any_Key");

        first.Should().Be(second);
    }
}
