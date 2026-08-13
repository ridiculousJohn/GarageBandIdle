using System.Collections.Generic;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // One modifiable number, offering the facts a selector can match it on
    // (design doc section 12, rule 11): its own id and tags, plus its owner's.
    //
    // The owner half is what makes a set of numbers reachable from the thing
    // holding them: a generator contributing cash AND fans has two numbers, and
    // "double the drummer" should reach both while "double the drummer's cash"
    // reaches one. Without the owner, the coarse buff would have to list every
    // line the generator holds and would silently miss any added later.
    //
    // A subject is built fresh by whoever owns the number, from state it already
    // has - never stored, never registered. Nothing has to be kept in step.
    public readonly struct ModifierSubject
    {
        private static readonly string[] NoTags = new string[0];

        public string Id { get; }
        public IReadOnlyList<string> Tags { get; }
        public string OwnerId { get; }
        public IReadOnlyList<string> OwnerTags { get; }

        public ModifierSubject(string id, IReadOnlyList<string> tags = null,
            string ownerId = null, IReadOnlyList<string> ownerTags = null)
        {
            Id = id ?? "";
            Tags = tags ?? NoTags;
            OwnerId = ownerId ?? "";
            OwnerTags = ownerTags ?? NoTags;
        }

        // Whether one selector term describes this number. THE SUBJECT DECIDES,
        // rather than the registry comparing strings, which is the seam a later
        // term form parses behind: `drummer.cash` needs the owner to resolve, and
        // it will be answered here without any caller changing.
        public bool Matches(string term)
        {
            if (string.IsNullOrEmpty(term))
                return false;

            return term == Id
                || term == OwnerId
                || Holds(Tags, term)
                || Holds(OwnerTags, term);
        }

        private static bool Holds(IReadOnlyList<string> tags, string term)
        {
            for (var i = 0; i < tags.Count; i++)
            {
                if (tags[i] == term)
                    return true;
            }
            return false;
        }

        public override string ToString()
            => OwnerId.Length == 0 ? Id : $"{OwnerId}/{Id}";
    }
}
