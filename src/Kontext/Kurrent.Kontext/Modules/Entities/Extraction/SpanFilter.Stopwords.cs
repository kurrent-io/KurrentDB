// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Frozen;

namespace Kurrent.Kontext.Entities.Extraction;

public static partial class SpanFilter {
    /// <summary>
    /// Single tokens that never name an entity on their own: pronouns, determiners, prepositions,
    /// conjunctions, adverbs, generic nouns and conversational filler.
    /// </summary>
    static readonly FrozenSet<string> Stopwords =
        """
        i me my myself we our ours ourselves you your yours yourself yourselves
        he him his himself she her hers herself it its itself
        they them their theirs themselves
        what which who whom this that these those
        am is are was were be been being have has had having do does did doing
        would should could ought might must shall will can
        a an the some any no every each either neither
        in on at by for with about against between into through during before after
        above below to from up down out off over under
        and but or nor so yet both not only than
        when where while if because although here there why how
        all few more most other such own same too very just also now then once
        always never often still already
        thing things stuff way ways something anything nothing
        someone anyone everyone nobody everybody somebody people person man woman men women guy guys
        time times day days year years today tomorrow yesterday
        one ones two first second third last next
        like really actually basically literally maybe probably perhaps
        well okay ok yes yeah yep nope um uh ah oh hmm hm er eh
        """.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToFrozenSet(StringComparer.Ordinal);
}
