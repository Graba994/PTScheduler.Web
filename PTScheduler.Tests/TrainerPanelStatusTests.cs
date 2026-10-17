using PTScheduler.Portal.Entities;
using PTScheduler.Portal.Services;
using Xunit;

namespace PTScheduler.Tests;

public class TrainerPanelStatusTests
{
    private static Tenant T(TenantStatus s) => new() { Status = s, BillingStatus = "none" };

    [Fact]
    public void Running_app_is_ok_and_hides_timeline_progress()
    {
        var st = TrainerPanelStatus.From(T(TenantStatus.Active));
        Assert.Equal("running", st.Key);
        Assert.Equal("ok", st.Tone);
        Assert.Equal(4, st.Step);
    }

    [Fact]
    public void Updating_wins_over_health()
    {
        var t = T(TenantStatus.Active); t.IsHealthy = false;
        Assert.Equal("updating", TrainerPanelStatus.From(t, updating: true).Key);
        Assert.Equal("down", TrainerPanelStatus.From(t).Key);
    }

    [Fact]
    public void Pending_states_explain_what_is_missing()
    {
        var pay = T(TenantStatus.Pending); pay.RegistrationPaymentGateway = "autopay";
        Assert.Equal("payment", TrainerPanelStatus.From(pay).Key);

        var card = T(TenantStatus.Pending); card.StripeCheckoutSessionId = "cs_1";
        Assert.Equal("card", TrainerPanelStatus.From(card).Key);

        var queued = T(TenantStatus.Pending); queued.QueuedReason = "limit dzienny";
        var st = TrainerPanelStatus.From(queued);
        Assert.Equal("queued", st.Key);
        Assert.Equal(2, st.Step);
    }

    [Fact]
    public void Suspended_reason_depends_on_billing_and_cancellation()
    {
        var unpaid = T(TenantStatus.Suspended); unpaid.BillingStatus = "past_due";
        Assert.Equal("danger", TrainerPanelStatus.From(unpaid).Tone);

        var cancelled = T(TenantStatus.Suspended); cancelled.CancelRequestedAt = DateTime.UtcNow;
        Assert.Equal("Uśpiona", TrainerPanelStatus.From(cancelled).Label);

        Assert.Equal(-1, TrainerPanelStatus.From(T(TenantStatus.Destroyed)).Step);
    }

    [Fact]
    public void Texts_are_gender_neutral()
    {
        var all = new[] { TenantStatus.Pending, TenantStatus.Provisioning, TenantStatus.Active, TenantStatus.Suspended, TenantStatus.Destroyed }
            .Select(s => TrainerPanelStatus.From(T(s)).Text);
        Assert.DoesNotContain(all, t => t.Contains("(a)") || t.Contains("/a "));
    }
}
