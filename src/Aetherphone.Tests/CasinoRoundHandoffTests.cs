using Aetherphone.Core.Aethernet;
using Aetherphone.Core.Aethernet.Contracts;
using Aetherphone.Core.Casino;
using Xunit;

namespace Aetherphone.Tests;

public sealed class CasinoRoundHandoffTests
{
    public enum Reply : byte
    {
        Spin,
        Gamble,
        Scratch,
        BarkeepStart,
        BarkeepFinish,
        Failure,
    }

    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ASpinThatLandsAfterTheCabinetLookedStaysInFlightUntilTheCabinetTakesIt()
    {
        var handoff = new CasinoRoundHandoff();
        var spin = new CasinoSlotsSpinDto(Granted: true, RoundId: "round1");
        handoff.BeginPost();

        Assert.Null(handoff.TakeSpin());
        await LandInBackground(handoff, landing => landing.Deliver(spin));

        Assert.True(handoff.InFlight);
        Assert.Same(spin, handoff.TakeSpin());
        Assert.False(handoff.InFlight);
    }

    [Fact]
    public async Task AFailedSpinStaysInFlightUntilTheCabinetTakesTheFailure()
    {
        var handoff = new CasinoRoundHandoff();
        handoff.BeginPost();

        Assert.False(handoff.TakeFailure());
        await LandInBackground(handoff, landing => landing.Fail());

        Assert.True(handoff.InFlight);
        Assert.True(handoff.TakeFailure());
        Assert.False(handoff.InFlight);
        Assert.False(handoff.TakeFailure());
    }

    [Theory]
    [InlineData(Reply.Spin)]
    [InlineData(Reply.Gamble)]
    [InlineData(Reply.Scratch)]
    [InlineData(Reply.BarkeepStart)]
    [InlineData(Reply.BarkeepFinish)]
    [InlineData(Reply.Failure)]
    public async Task EveryRoundReplyHoldsTheRoundUntilItIsTaken(Reply reply)
    {
        var handoff = new CasinoRoundHandoff();
        handoff.BeginPost();

        await LandInBackground(handoff, landing => Deliver(landing, reply));

        Assert.True(handoff.InFlight);
        Assert.True(Take(handoff, reply));
        Assert.False(handoff.InFlight);
    }

    [Fact]
    public async Task APostThatEndsWithoutAReplyReleasesTheRound()
    {
        var handoff = new CasinoRoundHandoff();
        handoff.BeginPost();

        Assert.True(handoff.InFlight);
        await LandInBackground(handoff, _ => { });

        Assert.False(handoff.InFlight);
    }

    [Fact]
    public async Task LeavingTheSlotsForAnotherScreenDropsTheirWaitingResultsAndReleasesTheRound()
    {
        var handoff = new CasinoRoundHandoff();
        handoff.BeginPost();
        await LandInBackground(handoff, landing => landing.Deliver(new CasinoSlotsSpinDto(Granted: true)));

        handoff.DropScratch();
        Assert.True(handoff.InFlight);

        handoff.DropSlots();
        Assert.False(handoff.InFlight);
        Assert.Null(handoff.TakeSpin());
    }

    [Fact]
    public async Task LeavingTheScratchKioskForAnotherScreenDropsItsWaitingCardAndReleasesTheRound()
    {
        var handoff = new CasinoRoundHandoff();
        handoff.BeginPost();
        await LandInBackground(handoff, landing => landing.Deliver(new CasinoScratchCardDto(Granted: true)));

        handoff.DropSlots();
        Assert.True(handoff.InFlight);

        handoff.DropScratch();
        Assert.False(handoff.InFlight);
        Assert.Null(handoff.TakeScratch());
    }

    [Fact]
    public async Task AWaitingGambleIsDroppedWithTheSlots()
    {
        var handoff = new CasinoRoundHandoff();
        handoff.BeginPost();
        await LandInBackground(handoff, landing => landing.Deliver(new CasinoSlotsGambleDto(Granted: true)));

        handoff.DropSlots();

        Assert.False(handoff.InFlight);
        Assert.Null(handoff.TakeGamble());
    }

    private static async Task LandInBackground(CasinoRoundHandoff handoff, Action<CasinoRoundHandoff> reply)
    {
        using var work = new StoreWork(nameof(CasinoRoundHandoffTests));
        var landed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        work.Run("reply", _ =>
        {
            reply(handoff);
            return Task.CompletedTask;
        }, () =>
        {
            handoff.EndPost();
            landed.SetResult();
        });

        await landed.Task.WaitAsync(ReplyTimeout);
    }

    private static void Deliver(CasinoRoundHandoff handoff, Reply reply)
    {
        switch (reply)
        {
            case Reply.Spin:
                handoff.Deliver(new CasinoSlotsSpinDto(Granted: true));
                break;
            case Reply.Gamble:
                handoff.Deliver(new CasinoSlotsGambleDto(Granted: true));
                break;
            case Reply.Scratch:
                handoff.Deliver(new CasinoScratchCardDto(Granted: true));
                break;
            case Reply.BarkeepStart:
                handoff.Deliver(new CasinoBarkeepStartDto(Granted: true));
                break;
            case Reply.BarkeepFinish:
                handoff.Deliver(new CasinoBarkeepFinishDto(Granted: true));
                break;
            default:
                handoff.Fail();
                break;
        }
    }

    private static bool Take(CasinoRoundHandoff handoff, Reply reply)
    {
        return reply switch
        {
            Reply.Spin => handoff.TakeSpin() is not null,
            Reply.Gamble => handoff.TakeGamble() is not null,
            Reply.Scratch => handoff.TakeScratch() is not null,
            Reply.BarkeepStart => handoff.TakeBarkeepStart() is not null,
            Reply.BarkeepFinish => handoff.TakeBarkeepFinish() is not null,
            _ => handoff.TakeFailure(),
        };
    }
}
