using Aetherphone.Core.Aethernet.Contracts;

namespace Aetherphone.Core.Casino;

internal sealed class CasinoRoundHandoff
{
    private volatile bool posting;
    private CasinoSlotsSpinDto? spin;
    private CasinoSlotsGambleDto? gamble;
    private CasinoScratchCardDto? scratch;
    private CasinoBarkeepStartDto? barkeepStart;
    private CasinoBarkeepFinishDto? barkeepFinish;
    private int failed;

    public bool InFlight => posting || AwaitingTake;

    private bool AwaitingTake =>
        Volatile.Read(ref spin) is not null
        || Volatile.Read(ref gamble) is not null
        || Volatile.Read(ref scratch) is not null
        || Volatile.Read(ref barkeepStart) is not null
        || Volatile.Read(ref barkeepFinish) is not null
        || Volatile.Read(ref failed) != 0;

    public void BeginPost()
    {
        posting = true;
    }

    public void EndPost()
    {
        posting = false;
    }

    public void Deliver(CasinoSlotsSpinDto result)
    {
        Volatile.Write(ref spin, result);
    }

    public void Deliver(CasinoSlotsGambleDto result)
    {
        Volatile.Write(ref gamble, result);
    }

    public void Deliver(CasinoScratchCardDto result)
    {
        Volatile.Write(ref scratch, result);
    }

    public void Deliver(CasinoBarkeepStartDto result)
    {
        Volatile.Write(ref barkeepStart, result);
    }

    public void Deliver(CasinoBarkeepFinishDto result)
    {
        Volatile.Write(ref barkeepFinish, result);
    }

    public void Fail()
    {
        Volatile.Write(ref failed, 1);
    }

    public CasinoSlotsSpinDto? TakeSpin()
    {
        return Interlocked.Exchange(ref spin, null);
    }

    public CasinoSlotsGambleDto? TakeGamble()
    {
        return Interlocked.Exchange(ref gamble, null);
    }

    public CasinoScratchCardDto? TakeScratch()
    {
        return Interlocked.Exchange(ref scratch, null);
    }

    public CasinoBarkeepStartDto? TakeBarkeepStart()
    {
        return Interlocked.Exchange(ref barkeepStart, null);
    }

    public CasinoBarkeepFinishDto? TakeBarkeepFinish()
    {
        return Interlocked.Exchange(ref barkeepFinish, null);
    }

    public bool TakeFailure()
    {
        return Interlocked.Exchange(ref failed, 0) != 0;
    }

    public void DropSlots()
    {
        Volatile.Write(ref spin, null);
        Volatile.Write(ref gamble, null);
    }

    public void DropScratch()
    {
        Volatile.Write(ref scratch, null);
    }
}
