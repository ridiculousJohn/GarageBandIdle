using System;
using System.Collections.Generic;
using RidiculousGaming.GarageBandIdle.Loop;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle
{
    // Which currencies a given chapter may reference, answered from content
    // alone (design doc section 12, rule 12). The runtime counterpart is
    // CurrencyRouter, which answers the same question about a LIVE economy by
    // asking the pools it holds - and that is exactly why it cannot serve here:
    // a pool exists only for the economy currently constructed, so asking it
    // about any other chapter resolves that chapter's currencies against the
    // running one's roster.
    //
    // Reachability is the chapter's authored roster plus every global currency,
    // which is the same rule EconomyContextFactory builds a real pool pair from:
    // the chapter's own CurrencyIds land in its pool, globals live in the
    // permanent pool, and a context reaches both. Derived from the definitions
    // rather than from a constructed pool, so it holds for a chapter that has
    // never been played and never will be during this session.
    //
    // Balances are deliberately absent. A currency's balance is a property of a
    // running economy, not of content, so the balance members throw rather than
    // answering zero - a caller that tries to EVALUATE a condition against a
    // validation context is asking a question this cannot answer, and a silent
    // zero would make every threshold gate read as unmet and look deliberate.
    public class ChapterCurrencies : ICurrencies
    {
        private readonly Dictionary<string, CurrencyDefinition> _definitions = new();
        private readonly Dictionary<string, CurrencyGroupDefinition> _groups = new();
        private readonly HashSet<string> _reachable = new();
        private readonly List<string> _rosterFaults = new();
        private readonly List<CurrencyDefinition> _roster = new();
        private readonly string _chapterId;

        // A null chapter is the orphan case: definitions no chapter lists have no
        // declaring roster to be measured against, so only existence is checked -
        // the same allowance the orphan pass makes for flags, and for the same
        // reason (no declaration list governs an orphan).
        public ChapterCurrencies(ContentDatabase database, ChapterDefinition chapter)
        {
            _chapterId = chapter?.Id;

            foreach (var group in database.CurrencyGroups.All)
            {
                if (!string.IsNullOrEmpty(group.Id))
                    _groups[group.Id] = group;
            }

            foreach (var currency in database.Currencies.All)
            {
                if (string.IsNullOrEmpty(currency.Id))
                    continue;

                _definitions[currency.Id] = currency;

                // a global currency is reachable from every chapter: it lives in
                // the permanent pool, which every economy context routes to
                if (_groups.TryGetValue(currency.GroupId ?? "", out var group)
                    && group.Placement == CurrencyPlacement.Global)
                    _reachable.Add(currency.Id);
            }

            if (chapter == null)
            {
                foreach (var id in _definitions.Keys)
                    _reachable.Add(id);
                return;
            }

            // The roster is filtered by the SAME rules EconomyContextFactory
            // applies when it builds the real pool, and for the same reason: a
            // roster entry the factory refuses never gets a balance in this
            // chapter's pool, so calling it reachable would have validation
            // accept content construction later rejects. Reachability and roster
            // legality are one computation, which is what stops the two from
            // disagreeing - an entry that is refused is not merely reported, it
            // is not reachable.
            foreach (var id in chapter.CurrencyIds)
            {
                if (string.IsNullOrEmpty(id))
                    continue;

                if (!_definitions.TryGetValue(id, out var currency))
                {
                    _rosterFaults.Add($"chapter '{_chapterId}' roster names unknown currency id '{id}'. Re-run the chapter import.");
                    continue;
                }

                // a global currency is held by the startup pool; a chapter
                // rostering one is asking for a second balance for the same id,
                // and every read would pick one of them arbitrarily
                if (_groups.TryGetValue(currency.GroupId ?? "", out var group)
                    && group.Placement == CurrencyPlacement.Global)
                {
                    _rosterFaults.Add($"chapter '{_chapterId}' roster names currency '{id}', whose group '{group.Id}' is placed Global - it is held by the startup pool and must not be in a chapter roster.");
                    continue;
                }

                _reachable.Add(id);
                _roster.Add(currency);
            }
        }

        // The chapter's roster as definitions, already filtered by the content
        // rules above - what EconomyContextFactory fills the chapter's pool from,
        // so the pool holds exactly what validation called reachable. The one
        // roster rule NOT applied here is shadowing the permanent pool: that is a
        // question about a pool object, not about content, so it stays with the
        // factory that holds the pool.
        public IReadOnlyList<CurrencyDefinition> RosterDefinitions => _roster;

        // Reports what the roster filtering above rejected. Separate from the
        // constructor because a roster is validated once per chapter, while the
        // resolver is asked about individual references many times - and because
        // boot validation, not construction, is where a LATER chapter's roster
        // gets looked at at all.
        public void ValidateRoster()
        {
            foreach (var fault in _rosterFaults)
                Debug.LogError($"ChapterCurrencies: {fault}");
        }

        // Undefined and unreachable are reported separately because they are
        // different mistakes with different fixes: the first is a typo or a
        // missing import, the second is a currency this chapter never declared -
        // one is fixed in the reference, the other in the chapter's roster.
        public bool ValidateReference(string id, string context)
        {
            if (string.IsNullOrEmpty(id) || !_definitions.ContainsKey(id))
            {
                Debug.LogError($"ChapterCurrencies: {context} references currency id '{id}', which resolves to no CurrencyDefinition asset.");
                return false;
            }

            if (_reachable.Contains(id))
                return true;

            Debug.LogError($"ChapterCurrencies: {context} references currency id '{id}', which chapter '{_chapterId}' does not declare - add it to the chapter's currency roster, or reference a currency the chapter owns (globals are reachable from every chapter).");
            return false;
        }

        // read from the group flag, the same single declaration CurrencyManager
        // reads it from - a currency's reset behavior is content, so it answers
        // here identically to how it would answer in a running economy
        public bool ResetsOnAlbumRelease(string currencyId)
            => !string.IsNullOrEmpty(currencyId)
               && _definitions.TryGetValue(currencyId, out var definition)
               && _groups.TryGetValue(definition.GroupId ?? "", out var group)
               && group.ResetsOnAlbumRelease;

        public CurrencyDefinition GetDefinition(string id)
            => !string.IsNullOrEmpty(id) && _definitions.TryGetValue(id, out var definition) ? definition : null;

        // ---- balances: not a property of content ------------------------------

        // Accepted and never fired, rather than refused: ConditionContext
        // subscribes to this in its constructor, and no balance can change here
        // for it to have missed. It is also one fewer subscription for a
        // validation context to leak - nothing is holding a reference back.
        public event Action<string, BigNumber> BalanceChanged
        {
            add { }
            remove { }
        }

        public BigNumber Get(string id) => throw Unsupported(nameof(Get));

        public void Add(string id, BigNumber amount) => throw Unsupported(nameof(Add));

        public void Set(string id, BigNumber value) => throw Unsupported(nameof(Set));

        public BigNumber GetLifetimeEarned(string id) => throw Unsupported(nameof(GetLifetimeEarned));

        private static NotSupportedException Unsupported(string member)
            => new($"ChapterCurrencies.{member}: balances belong to a running economy, not to content. "
                   + "This resolver answers reference questions only - evaluating a Condition needs a real CurrencyManager or CurrencyRouter.");
    }
}
