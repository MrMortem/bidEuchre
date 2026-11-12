using BidEuchre.Core;

namespace BidEuchre.App;

/// <summary>Contract exposure survives table redraws. Indices match the artwork: heart, club, spade, diamond.</summary>
public sealed class SuitBackdropMotion
{
    private readonly double[] _from = new double[4];
    private readonly double[] _target = new double[4];
    private double _changedAt;

    public void SetContract(Contract? contract, double now)
    {
        Suit[] suits = [Suit.Hearts, Suit.Clubs, Suit.Spades, Suit.Diamonds];
        var target = suits.Select(suit => contract?.Mode switch
        {
            ContractMode.Low => -1d,
            ContractMode.High => 1d,
            ContractMode.Trump when contract.Trump == suit => 1d,
            _ => 0d
        }).ToArray();
        if (_target.SequenceEqual(target)) return;
        for (var index = 0; index < 4; index++) _from[index] = Exposure(index, now);
        target.CopyTo(_target, 0);
        _changedAt = now;
    }

    // -1: offscreen, 0: original corner, +1: fully emerged.
    public double Exposure(int index, double now, bool animate = true)
    {
        if (!animate) return _target[index];
        var t = Math.Clamp((now - _changedAt) / .65, 0, 1);
        var eased = t * t * (3 - 2 * t);
        return _from[index] + (_target[index] - _from[index]) * eased;
    }
}
