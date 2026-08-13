using System;
using System.Collections.Generic;
using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // What a modifier says about the numbers it reaches (design doc section 12,
    // rule 11): a list of terms, any one of which reaching a number is enough. A
    // term is an id or a tag - the same open content vocabulary as every other
    // reference in the game, and nothing finer. Anything narrower than "this
    // thing" is said by giving the narrower thing its own id and naming that.
    //
    // Naming numbers this way is what survives one generator feeding two currencies:
    // "double the drummer's output" has to say WHICH output, and it can, because
    // every modifiable number carries an id of its own. There is no enum here on
    // purpose - giving the game a new modifiable number means naming that number,
    // not adding a member every reader then has to learn.
    //
    // AN EMPTY SELECTOR MATCHES EVERYTHING in reach, which is what makes "double
    // all generator output" or "-99% cost for this tier" placement rather than an
    // authored id list that new content silently falls out of. Empty is a
    // deliberate authoring act, not a default: content that declares no terms at
    // all is refused where it is read (see ChapterJsonImporter), so a forgotten
    // key cannot become "buff the whole game".
    [Serializable]
    public struct ModifierSelector : IEquatable<ModifierSelector>
    {
        [SerializeField]
        [Tooltip("Ids or tags this reaches. EMPTY reaches every number in scope.")]
        private string[] _terms;

        public static ModifierSelector Everything => new ModifierSelector(null);

        public ModifierSelector(IEnumerable<string> terms)
        {
            _terms = Normalize(terms);
        }

        public int TermCount => _terms?.Length ?? 0;

        public string Term(int index) => _terms[index];

        // reads as its own sentence at call sites deciding whether to warn
        public bool ReachesEverything => TermCount == 0;

        // Whether this reaches one number. ANY term matching is enough: the list
        // is a list of names, so ["cash_rate","fans_rate"] reaches both, exactly
        // as it reads.
        //
        // It is deliberately not an intersection. That was the facet-era rule,
        // where ["cash","rate"] meant the currency AND which of its numbers - two
        // halves of one address, both of which had to hold. With names there are no
        // halves, and narrowing has a better answer: a set gets a TAG on the lines
        // that belong to it (rule 11), so "the rhythm section's cash" is one term
        // on exactly the lines meant, not two terms intersected. An intersection
        // rule would also make the honest reading of a name list - all of these -
        // reach nothing at all, since no one number carries two ids.
        //
        // The per-term question is the SUBJECT's to answer, so this never compares
        // strings itself - one seam, which is what keeps a later term form from
        // touching the registry, the composition or any display.
        public bool Matches(in ModifierSubject subject)
        {
            if (_terms == null)
                return true;

            foreach (var term in _terms)
            {
                if (subject.Matches(term))
                    return true;
            }
            return false;
        }

        private static string[] Normalize(IEnumerable<string> terms)
        {
            if (terms == null)
                return null;

            List<string> kept = null;
            foreach (var term in terms)
            {
                // An empty term would match nothing and so would silently make the
                // whole selector unreachable - the opposite of the empty SELECTOR,
                // which reaches everything. Dropping it here means one place knows
                // that, instead of every reader guarding against it.
                if (string.IsNullOrEmpty(term))
                    continue;

                kept ??= new List<string>();
                if (!kept.Contains(term))
                    kept.Add(term);
            }
            return kept?.ToArray();
        }

        public bool Equals(ModifierSelector other)
        {
            if (TermCount != other.TermCount)
                return false;

            for (var i = 0; i < TermCount; i++)
            {
                if (_terms[i] != other._terms[i])
                    return false;
            }
            return true;
        }

        public override bool Equals(object obj) => obj is ModifierSelector other && Equals(other);

        public override int GetHashCode()
        {
            var hash = 17;
            for (var i = 0; i < TermCount; i++)
                hash = (hash * 397) ^ _terms[i].GetHashCode();
            return hash;
        }

        // the form every modifier message names a selector by
        public override string ToString()
            => TermCount == 0 ? "*" : string.Join("+", _terms);
    }
}
