using BidEuchre.App;
using BidEuchre.Core;

internal static class SuitBackdropTests
{
    public static void ContractTransitions()
    {
        var motion = new SuitBackdropMotion();
        motion.SetContract(new Contract(BidLevel.Three, ContractMode.Trump, Suit.Hearts), 0);
        Check(motion.Exposure(0, .3) is > 0 and < 1, "Trump slides rather than jumps");
        Check(motion.Exposure(1, 1) == 0 && motion.Exposure(0, 1) == 1, "Only trump emerges");
        motion.SetContract(new Contract(BidLevel.Three, ContractMode.High, null), 1);
        Check(Enumerable.Range(0, 4).All(i => motion.Exposure(i, 2) == 1), "High reveals all suits");
        motion.SetContract(new Contract(BidLevel.Three, ContractMode.Low, null), 2);
        var halfway = motion.Exposure(0, 2.3);
        motion.SetContract(new Contract(BidLevel.Three, ContractMode.Low, null), 2.3);
        Check(motion.Exposure(0, 2.3) == halfway, "Polling must not restart the slide");
        Check(Enumerable.Range(0, 4).All(i => motion.Exposure(i, 3) == -1), "Low hides all suits");
        motion.SetContract(null, 3);
        Check(Enumerable.Range(0, 4).All(i => motion.Exposure(i, 4) == 0), "New hands and menu restore idle suits");
        motion.SetContract(new Contract(BidLevel.Three, ContractMode.Trump, Suit.Spades), 4);
        Check(motion.Exposure(2, 4, animate: false) == 1, "Motion off applies the contract immediately");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
