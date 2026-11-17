using System.Collections.Generic;

namespace RidiculousGaming.GarageBandIdle
{
    // What ONE completion of an action list changes, as far as a condition can
    // read it (design doc 12.4/12.5): each action describes its own execution
    // into this, and a condition asks how many repetitions of it until its
    // answer differs (Condition.StepsUntilChange). That distance is a segment
    // boundary, so a repeating bar settles every completion up to it at once
    // and re-reads from the new state (12.7).
    //
    // Everything is held by reference to the definitions and nodes the
    // describing action already holds: a deposit is keyed by the target and
    // the home the payment lands at, a flag and a timer by the id the write
    // names. Nothing here resolves an id or looks anything up.
    //
    // Unknown is the honest answer of a kind that cannot say what it changes:
    // every condition then re-reads after each completion, which is always
    // correct. Empty is a list that changes nothing a condition reads.
    public class CompletionDelta
    {
        private readonly List<(Definition target, ScopeState home, BigNumber amount)> deposits = new();
        private readonly HashSet<string> flags = new();
        private readonly HashSet<string> timers = new();

        public bool Unknown { get; private set; }

        public bool Empty => !Unknown && deposits.Count == 0 && flags.Count == 0 && timers.Count == 0;

        // A payment one completion makes to a target at its home: a currency's
        // balance and earned total, or a generator's granted count. Two
        // payments to one (target, home) are one figure.
        public void AddDeposit(Definition target, ScopeState home, BigNumber amount)
        {
            for (var i = 0; i < deposits.Count; i++)
            {
                if (deposits[i].target != target || deposits[i].home != home)
                    continue;
                deposits[i] = (target, home, deposits[i].amount + amount);
                return;
            }
            deposits.Add((target, home, amount));
        }

        public void AddFlag(string flagId) => flags.Add(flagId);

        public void AddTimer(string timerId) => timers.Add(timerId);

        public void MarkUnknown() => Unknown = true;

        // What one completion pays this target at this home; zero when it pays
        // nothing there.
        public BigNumber DepositTo(Definition target, ScopeState home)
        {
            for (var i = 0; i < deposits.Count; i++)
                if (deposits[i].target == target && deposits[i].home == home)
                    return deposits[i].amount;
            return BigNumber.Zero;
        }

        public bool SetsFlag(string flagId) => flags.Contains(flagId);

        public bool ExtendsTimer(string timerId) => timers.Contains(timerId);
    }
}
