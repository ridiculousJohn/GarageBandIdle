# Ctrl C: Chapters 1-8
## Gameplay reference reconstructed from Android version 1.12.2 (146)

This is a static reconstruction of the four APKs you supplied. It covers the chapter economies, generator families, upgrades, challenges, reset layers, completion conditions, replay system, and story through Chapter 8. The intended progression routes are interpretations of those rules, not timed or experimentally optimized playthroughs.

**The main finding:** these are eight different incremental-game systems connected by achievement tokens. A chapter's local prestige currencies do not become the next chapter's currencies. The persistent benefit of finishing and replaying chapters is the shared token system.

### How to read the evidence

- **[D] Direct data:** a recovered string, enum, object, price, target, or bundled configuration default.
- **[C] Code-derived:** behavior reconstructed from named functions and their ARM64 instructions. Stronger than guessing from descriptions, but not a live gameplay test.
- **[I] Inference:** a recommended approach or design interpretation.
- **[U] Unresolved:** the available analysis does not establish the exact detail. These are called out rather than filled with plausible numbers.

Tables of names, base prices, rates, and challenge text are **[D]** unless marked otherwise. Reset tables and formulas are **[C]**. The walkthrough steps are **[I]**. Story summaries are **[D]**, paraphrased from dialogue.

The guide is detailed, but is **not a claim that every visibility condition and every dynamically generated upgrade cost has been fully reconstructed**. The remaining gaps are listed at the end. No APK was modified or executed, and no personal save or live remote settings were obtained.

### Companion files

- [Data appendix](Ctrl-C-Data-Appendix.md): constant generator and quest records and recovered upgrade cost schedules.
- [Story transcript](Ctrl-C-Story-Transcript.md): recovered conversations through Chapter 8.
- [Structured recovered data](Ctrl-C-Recovered-Data.json): machine-readable records and APK fingerprints.
- [Evidence archive](Ctrl-C-Evidence.zip): annotated chapter assembly, resolved constants, shared mechanics, and provenance. The `.dart` filenames contain disassembly, not original Dart source. The whole-program constant dumps also contain later-chapter material.

## Contents

1. [Shared systems and replay](#shared-systems-and-replay)
2. [Chapter 1: Library Of Stacklandia](#chapter-1-library-of-stacklandia)
3. [Chapter 2: Calling Yourself Agile Over Being Agile](#chapter-2-calling-yourself-agile-over-being-agile)
4. [Chapter 3: Embrace The Yak Shave](#chapter-3-embrace-the-yak-shave)
5. [Chapter 4: Unicorns Don't Exist](#chapter-4-unicorns-dont-exist)
6. [Chapter 5: Recurse, Recurse, Recurse](#chapter-5-recurse-recurse-recurse)
7. [Chapter 6: Free As In Beer](#chapter-6-free-as-in-beer)
8. [Chapter 7: TL;DR](#chapter-7-tldr)
9. [Chapter 8: Glory To The Coming Age Of Free Software](#chapter-8-glory-to-the-coming-age-of-free-software)
10. [Coverage, uncertainties, and source map](#coverage-uncertainties-and-source-map)

## Shared systems and replay

### What was in the APKs

The package is `com.amorfatite.pretentious`, version **1.12.2**, build **146**. The bundled GitInfo names revision `fcf42ec5d6d259b671768f09a67ac5423c388625`, Flutter `3.44.4`, and date `2026-07-01`. [D]

`base.apk` contains the Android shell and Flutter assets. The ARM64 split contains the decisive gameplay artifact: `libapp.so`, a compiled Dart AOT snapshot. The language and density splits supply localized/platform and density resources. There is no complete, conveniently editable chapter JSON in the base APK. The rules were recovered from the native snapshot's objects and code. [C]

### Numbers, production, and purchases

The game uses `CustomNum`, which stores the natural logarithm of large values. This lets it represent extreme quantities without ordinary floating-point overflow. In the tables, values decoded from rounded object dumps are shown approximately; the evidence retains logarithmic constants where available. For example, the Chapter 8 ending target decodes to about five billion, not a deliberately meaningful target of exactly 4,999,999,750. [C]

Separate these concepts:

| Term | Meaning |
|---|---|
| Current currency | The spendable balance now. |
| Cumulative currency | The amount accumulated within the relevant reset scope. Spending may leave this unchanged. Several prestige formulas use it. |
| Purchased generators | Copies bought with currency; commonly drive escalating prices. |
| Bonus/generated generators | Copies supplied by another generator or a reward. Often excluded from price growth or count-based bonuses. |
| Timed bar | Produces a batch after a cycle; may need an input or automatic-start unlock. |
| Passive generator | Produces continuously according to its rate and boosts. |

Generator purchase curves are geometric: `next cost = base cost * r^purchased count * applicable cost multipliers`. **Chapters 1-3 use `r = 1.6`; Chapters 4, 5, 7, and 8 use `r = 1.3`. Chapter 6 uses `r = 1.1` for ordinary generators and `r = 10` for its upper, bonus-producing tier.** Chapter 1's Literally Unusable reward changes its factor to 1.584. First-copy special cases and chapter-specific discounts can apply. These curves should not be substituted for every upgrade, share purchase, or funding round; those have separate rules. [C]

For a geometric generator whose cost multiplier stays fixed during a bulk purchase, buying `k` copies at count `n` costs `base * multiplier * r^n * (r^k - 1) / (r-1)`. This is the mathematical consequence of that curve, not a separate extracted game rule. [I]

### Finishing, replaying, and achievement tokens

The completion interface says that finishing resets the current chapter and awards an achievement token. Completion is distinct from a local Rewrite, Promotion, Rebase, or other prestige. Local prestige preserves selected chapter systems; a chapter restart recreates the chapter's local state. Other chapters and global achievement-token progress are separate. Story playback history is also stored globally. [D/C]

The ordinary first completion initializes that chapter's token entry to **1**. For an existing entry, `CoreData.addCompletionToken` adds:

`1 + 0.1*d + 0.012*d^1.5`

where `d` is the chapter's stored difficulty index passed by the completion routine. At difficulty index zero, this is one token. Thus the UI's generic "1 token" wording is not the whole difficulty-dependent calculation. [C]

The bundled default token bonus is **5%**. Ignoring bonus-token assignment, penalty settings, and chapter-specific overrides, the recovered boost calculation multiplies chapter contributions:

`global boost = product over chapter entries of (1 + 0.05 * tokens in that entry)`

Tokens within an entry add; factors across entries multiply. With this simplified default, one token in each of eight chapters gives `1.05^8`, about **1.477x**. Eight tokens concentrated in one entry give **1.4x**. These are illustrations, not a reconstruction of your save. [C/I]

The actual calculation also handles assignable Bonus tokens, a configurable penalty, an optional `ch_<chapter>_multiplier`, and a user preference exponent clamped to 0-1. The final effective multiplier is floored at one before that exponent is applied. Do not assume that every visible +5% is an independent multiplicative factor. [C]

**Practical replay implication [I]:** replaying an earlier chapter can make a later chapter faster. It does not supply that later chapter's Knowledge, Money, Shares, or upgrades. Because each chapter has different local bottlenecks, raw token count alone does not predict its completion time.

### Remote settings and time

Bundled defaults include `CompletionTokenBonusPercent = 5`, `NewCompletionTokenSystem = false`, `CompletionTokenPenaltyPercent = 0`, `show_waterfall_methods = false`, and Chapter 2 difficulty multiplier 1. Overclock defaults include a four-hour quantity, a 24-hour maximum, and a 2x multiplier. These are defaults, not proof of the values served to your phone. [D]

Automatic bar cycling, automatic purchasing, automatic prestige, offline simulation, and overclocking are separate systems. A chapter offering one does not imply all the others. Chapter 4 explicitly implements offline funding-round automation; Chapter 1 Delegation explicitly describes purchases while the game is open. Exact global offline limits and all timing edge cases were not fully reconstructed. [D/U]

### Completion overview

| Chapter | Local finish condition recovered | Main local reset layers |
|---|---:|---|
| 1 | About `5.80416e52` Lines | Rewrite; Project |
| 2 | About `3.16896e54` Lines | Promotion |
| 3 | About `3.67856e35` Career Capital | Networking, Storage, Scripting prestige |
| 4 | Complete an IPO round; about `2.67075e24` Lines | Funding rounds |
| 5 | About `8.71080e11` Features | Knowledge reset; Reorg; Deploy |
| 6 | About `6.27220e22` Commits | Rebase; Conference |
| 7 | About `7.05844e15` Power | Rewrite |
| 8 | About `5e9` Shares | Liquidate; Buy Shares; Bicho failure reset |

These are completion targets, not the amount required for an intermediate prestige. Additional UI visibility/story gates can precede the final button. Finishing each chapter feeds the same global token system; there is no recovered chapter-specific permanent resource reward that replaces it.

## Chapter 1: Library Of Stacklandia

### Economy

The first chapter builds three production layers. **Lines** buy keyboards and other coding equipment. A **Rewrite** converts accumulated Lines into **Knowledge**. Knowledge buys generators of **Data**. A higher **Project** reset converts accumulated Data into **Career Capital**, which buys **Tools** generators. Tools strengthen the lower layers. [D/C]

| Layer | Generator | Base cost | Base output per second |
|---|---|---:|---:|
| Lines | Mouse | 100 Lines | 10 Lines |
| Lines | Keyboard | 345 Lines | About 34.9369 Lines |
| Lines | Mechanical Keyboard | 1,230 Lines | About 125.81 Lines |
| Lines | Clickier Keyboard | 8,880 Lines | About 917.412 Lines |
| Lines | RGB Keyboard | 87,400 Lines | `87,400 * exp(-2.26)` Lines |
| Data | Notes | 100 Knowledge | 1 Data |
| Data | Files | 2,157.72 Knowledge | About 19.4195 Data |
| Data | Code Snippets | 14,426.3 Knowledge | About 115.410 Data |
| Data | Macros | 28,940.6 Knowledge | About 202.584 Data |
| Tools | Wiki | 100 Career Capital | 1 Tool |
| Tools | Discussion Board | 2,157.72 Career Capital | About 21.5772 Tools |
| Tools | Video Conferencing | 15,398.1 Career Capital | About 153.981 Tools |
| Tools | Knowledge Bank | 197,600 Career Capital | About 1,976 Tools |
| Tools | Time Tracking | 1.36001e6 Career Capital | About 13,600.1 Tools |

These are pre-upgrade, pre-token rates. The RGB constructor uses 87,400 even though an internal static identifier resembles 87,480; the constructed value is what is reported here. [C]

Recovered stat multipliers and prestige formulas, using cumulative quantities within the relevant reset scope: [C]

- Line production includes `(1 + 0.01*Knowledge) * (1 + 0.01*Data) * (1 + 0.01*Tools)`.
- Rewrite gain: `cumulative Lines * exp(-9.7245) * (1 + 0.01*Tools) * (1 + 0.01*Career Capital)`.
- Project gain: `cumulative Data * exp(-11.7245) * (1 + 0.01*Tools)`.
- `exp(-9.7245)` is about `5.98003e-5`; `exp(-11.7245)` is about `8.09309e-6`.

### Upgrades and challenges

| Upgrade | Cost | Effect |
|---|---:|---|
| PARA Organization | 328.595 Knowledge | Five bonus copies of each tier-one generator, without increasing its purchase price. |
| Cloud Notes | 4,889.40 Knowledge | Doubles tier-one production. |
| Markdown | 21,912.8 Knowledge | Count-based tier-one production bonus; text specifies 1% per generator owned. |
| Syntax Highlighting | 45,615.4 Knowledge | Tier-one and tier-two generator costs reduced by 99%. |
| Delegation, three levels | 130.843; 465.448; 3,885.47 Career Capital | Tier-one autobuy while open: once per second, five times per second, then every tick. |
| Deadlines | 456.231 Career Capital | Tier-two cost reduction described as 1% per level. |
| Meetings | 3.40185e8 Career Capital | Tier-three costs reduced by 99.99%. |
| Snacks | 1.86930e11 Career Capital | Doubles all generator yields. |

Markdown/Deadlines' exact interpretation of every count/level operand is less certain than their text and the other fixed multipliers. [U]

| Quest | Restriction | Target | Reward |
|---|---|---:|---|
| 4 Day Work Week | Lines 20% slower | `1.50202e14` Lines | 100% increase to line speed. |
| Waterfall Methods | A purchase drops output to zero, recovering over one minute | `2.85613e18` Lines | Double all yields. Remote-gated; bundled visibility default is false. |
| Literally Unusable | RGB Keyboard and Macros yield zero | `2.18229e25` Lines | Lower per-copy price growth. |

Literally Unusable changes the geometric factor from **1.6 to 1.584**, so its effect compounds with purchased count. It is not simply a one-time 1% discount. [C]

### Reset contract

| Action | Cleared | Preserved / awarded |
|---|---|---|
| Rewrite | Current and cumulative Lines; purchased Mouse through RGB Keyboard | Awards Knowledge. Keeps Knowledge/Data/Capital/Tools layers, their generators, upgrades, and completed challenges. Owned unlock callbacks run again, including starting bonuses. |
| Project | Current and cumulative Lines, Knowledge, Data; bought generators through Macros; tier-one bonus copies; PARA, Cloud Notes, Markdown, Syntax Highlighting | Awards Career Capital. Keeps Capital, Tools, tool generators, Delegation and later upgrades, and quest completions. Remaining unlock callbacks are reapplied. |
| Complete chapter | Chapter-local run | Global token progress and chapter access remain. |

### Progression loop [I]

1. Establish Line production, then Rewrite once the resulting Knowledge buys a useful part of the Data layer.
2. Develop Data while recycling the Line layer. PARA and the lower-layer cost reduction make restarts less expensive.
3. Use the first Project resets to establish Tools. This creates a multiplier on both lower production and future prestige income.
4. Add Delegation to reduce repeated clicking. Buy the very large tier-three discount before treating the last tool costs as a pure waiting problem.
5. Complete available quests when their permanent benefit repays the restricted run. Use the final multiplicative growth to reach the Line target.

### Story

**Introduction** establishes the programming workplace and the copy/paste progression joke. **Moving On Up** carries the player into a more organizational role. The mechanics turn personal equipment, accumulated knowledge, and organizational tools into successive layers of leverage; this interpretation is thematic rather than an extra rule. [D/I]

## Chapter 2: Calling Yourself Agile Over Being Agile

### Economy and teams

This chapter replaces the first chapter's continuous keyboard economy with **timed teams**. Teams produce **Lines**. The secondary generators produce **Data**, which increases Lines. **Promotion** awards **Experience**, another line multiplier. Each Data earned and each Experience supplies a 1% line boost according to the UI; the separate boost factors combine in the production calculation. [D/C]

| Team | Base cycle | Lines per cycle | Base level cost, Lines | Separate hire/auto cost, Lines |
|---|---:|---:|---:|---:|
| Backend Developer | 1 s | 1.0575 | 4.23 | 500 |
| Frontend Developer | 4 s | 19.624 | 24.53 | 15,000 |
| Quality Assurance | 7 s | 19,250 | 55,000 | `1.97e14` |
| Sales | 25 s | `1.00478e8` | `7.25e15` | `1.04e19` |
| Networking | 30 s | `4.05356e10` | `7.42e24` | `2.04e26` |
| Marketing | 90 s | `6.01333e12` | `1.99e34` | `5.48363e38` |
| Design | 250 s | `1.32452e17` | `7.04e42` | `9.63667e52` |

The two price columns are distinct fields in `GeneratorInfo`; do not read the large second price as the team's first level price. Ordinary level costs grow geometrically and include boosts. Automatic-start state is separate from purchased levels. [C]

| Data generator | Base cost in Lines | Base Data rate |
|---|---:|---:|
| Mondays | 1,500 | About 0.740818 |
| Tuesdays | 3,500 | About 2.33333 |
| TGIF | 4,000,000 | About 8.70525 |

### Upgrades and sprints

- **HR:** about `6.08101e8` Lines; bulk purchase options and multipliers for progress upgrades.
- **Task Switching:** taps add progress time to bars. The function computes `[0.23*(n+1)]^0.54` seconds for its integer argument `n`; caller indexing determines the current/next-level preview. Prices are five Line payments (`1.01207e13`, `2.22922e17`, `4.91019e21`, `1.08154e26`, `2.38225e30`), then two Experience payments (`6.90029e11`, `1.02145e15`).
- **Stand Up:** about `1.35650e7` Experience; Promotion retains half of Data instead of deleting it, and permits repeated Sprint completions.
- **Micromanagement:** about `6.05038e9` Data; all teams start with automation and Data gain is multiplied by ten.

Each Sprint is a **five-minute challenge**. Failure automatically cancels it. The first reward doubles Lines and Data; subsequent levels double them again per completion. [D]

| Sprint | Line target |
|---|---:|
| 1 | `5.62e11` |
| 2 | `7.76e20` |
| 3 | `2.93e24` |
| 4 | `2.30e27` |
| 5 | `3.33825e30` |
| 6 | `7.35299e34` |
| 7 | `2.40370e41` |

### Promotion and persistence

`Experience awarded = cumulative Lines * exp(-10.9241383)`, approximately `cumulative Lines / 55,500`. [C]

Promotion clears current and cumulative Lines, bar progress, and the team-upgrade map. Data current/cumulative totals are deleted, or halved with Stand Up. The routine explicitly removes purchased Data generators 7-9 and purchased generator entries 1-3. Automation is cleared unless Micromanagement is owned, in which case it enables all seven teams. Experience, the persistent unlocks, and completed Sprint records survive. [C]

**Implementation detail:** Chapter 2 stores bar upgrades, continuous generators, and automation in separate containers. "Everything above" in the UI is less precise than the actual reset. The numeric entry removals are included so the rule is not falsely generalized to a single universal generator list.

### Progression loop [I]

1. Cycle and upgrade the early developers while building Data.
2. Promote to get enough Experience to materially shorten the rebuild, then invest in the next team.
3. Use HR and Task Switching to make the slow bars manageable.
4. Obtain Stand Up before leaning heavily on repeated Sprints or accumulating Data between Promotions.
5. Treat Micromanagement as a major reduction in restart friction. Advance through the later teams and Sprint multipliers toward `3.16896e54` Lines.

### Story

**A Business Deal** introduces Jeff Bicho and the RWS business relationship. **Meetings** shifts the emphasis from doing work to coordinating it, leading into the management economy of Chapter 3. [D]

## Chapter 3: Embrace The Yak Shave

### Economy and divisions

Three divisions generate **Progress**, which meetings convert into **Career Capital**. **Networking Boost**, **Storage Boost**, and **Scripting Boost** are persistent local prestige resources. Their displayed effects are +1% Networking speed, +1% Storage yields, and +1% Scripting yields per point. [D]

**Attend Meeting** starts as a one-second cycle that consumes one Progress and produces one Career Capital. Faster/larger meetings alter this, and Pizza Rule rewards eventually remove the Progress consumption. [D/C]

| Division / component | Base cycle where specified | Raw Progress value | Base Career Capital price |
|---|---:|---:|---:|
| Networking main bar | 12 s | 1 | Opened by progression |
| Internal Networking | 4 s | 25.2797 | 30 |
| External Networking | 6 s | 38.5735 | 9,897.13 |
| Networking Protocols | 10 s | 64.4008 | 3.75521e6 |
| Storage main bar | 12 s | 10.8482 | Opened by progression |
| HDD | Passive component | 85 | 5.58735e7 |
| SSD | Passive component | 83.6392 | 6.13076e8 |
| CDN | Passive component | 75.6799 | 1.31882e9 |
| Memory Cache | Passive component | 75.6799 | 7.20049e10 |
| Scripting main bar | 12 s | 80.1580 | Opened by progression |
| Javascript | Passive component | 4,640.84 | 1.09760e22 |
| C# | Passive component | 5,046.81 | 1.20367e25 |
| C++ | Passive component | 5,577.59 | 5.82185e28 |
| Rust | Passive component | 6,812.49 | 5.05239e31 |

The division main bars and component upgrades also affect one another: the recovered interface describes component upgrades increasing the accompanying main bar's speed, and a separate multiplier on main-bar yields. The raw Progress column is not a complete marginal-output comparison. The exact per-component cross-boost coefficients remain unresolved. [D/U]

### Upgrades

| Upgrade | Recovered role |
|---|---|
| Schedules | Automates meetings. |
| VIPs | Additional tapping advances time. |
| Faster Meetings | Increases meeting speed. |
| Larger Meetings | Raises meeting yield and its Progress cost. |
| Overtime | Raises Progress growth. |
| WFH | Reduces all generator costs by 99%; later levels apply further reductions. |
| Better Cables | Component upgrades increase the speed of their accompanying main bar. |
| Better Switches | Increases the yields of each main bar. |
| Optimization | Large component/bar speed improvement; recovered fixed factor 10x in its upgrade path. |
| Hardware Acceleration | Large main-bar speed improvement; recovered fixed factor 50x in its upgrade path. |
| PTO | Unlocked by Bureaucracy; multiplies meeting yields and Progress growth. |

Some dynamic multipliers are explicitly recoverable. For an owned level `n`, Faster Meetings uses `1.397941876^n + 1.8^n`; Overtime uses `1.033456617^n + 2.873444387^n`. Larger Meetings starts from `1.262266763^n + 2.6^n`, then applies challenge adjustments. The unowned branch returns one; do not evaluate these formulas at zero and infer a free 2x bonus. [C]

### Challenges

- **Two / Four / Eight / Sixteen / Thirty Two Pizza Rule:** meeting yields become 1/2, 1/4, 1/8, 1/16, and 1/32. Career Capital targets are respectively `8.66883e9`, `2.94728e19`, `5.05793e24`, `4.26253e31`, and `1.72854e32`. Rewards allow meetings without consuming Progress and make current Progress improve Career Capital production. The per-Progress coefficient doubles across these stages: `2^-24`, `2^-23`, `2^-22`, `2^-21`, `2^-20`. The resulting factor is `1 + current Progress * coefficient`. [D/C]
- **Management Styles:** division main bars do nothing; target about `9.63471e25` Career Capital; reward 10x meeting speed and yields. [D]
- **Bureaucracy:** purchasing makes yields fall to zero and recover over one minute; target about `4.25152e32` Career Capital; reward unlocks PTO. [D]

### Division prestige

Each prestige consumes the common **current and cumulative Progress and Career Capital**, but clears its own division's purchased components rather than all three divisions. Prestige resources and other divisions' machinery survive. [C]

| Reset | Division-specific clearing / effect |
|---|---|
| Networking | Clears component entries 2-4 and Better Cables/Better Switches. Can initialize the Storage main bar. |
| Storage | Clears component entries 6-9. |
| Scripting | Clears component entries 11-14. |

The UI describes Scripting prestige as clearing scripting "generators/unlocks". The inspected routine explicitly removes the generator entries but does not explicitly remove the two named scripting unlock entries. This is a code/text discrepancy; I have not silently treated the UI sentence as the authoritative reset list. [C/U]

Prestige gains derive from cumulative Progress, with base logarithmic factors `exp(-11.8)`, `exp(-15.8)`, and `exp(-24.8)` for Networking, Storage, and Scripting respectively, divided by `1 + 0.01*existing corresponding boost`. A shared softcap applies to a candidate gain `x` against `a = existing points + 100`:

`gain = x` if `x <= a`; otherwise `gain = a + (x-a)*sqrt(a/x)`.

This is a diminishing-return transformation, not a hard maximum of 100. [C]

### Progression loop [I]

1. Balance Progress production against meeting consumption. Making meetings larger without enough Progress can create a supply bottleneck.
2. Automate meetings and expand Networking; use Networking prestige to open and strengthen the next division.
3. Develop Storage and Scripting while keeping useful machinery in the other divisions across each local reset.
4. Pizza rewards change Progress from a consumed input toward a retained multiplier. That changes the value of banking Progress.
5. Complete Management Styles and Bureaucracy for their major boosts, then reach about `3.67856e35` Career Capital.

### Story

**Middle Management** and **Monotony** satirize process overtaking productive work. The player leaves to build a search business, **FlexiSearch**, setting up Chapter 4's startup financing loop. [D]

## Chapter 4: Unicorns Don't Exist

### Economy

**Lines** are the round's immediate production target. **Money**, earned by completing funding rounds, pays for durable improvements. **Velocity** increases line speed by 1% per point. Line generators include **Variables, Function, Class, Pattern**; velocity generators include **Faster SSD, More GHz, Larger Monitor**. [D]

| Generator | Base cost | Base rate before boosts |
|---|---:|---:|
| Variable | 690 Lines | 2 Lines/s |
| Function | 1,380 Lines | 4 Lines/s |
| Class | About 50,950 Lines | 168.429 Lines/s |
| Pattern | About 1.51880e8 Lines | 378,001 Lines/s |
| Faster SSD | About 6.28773 Money | 0.0117647 Velocity/s |
| More GHz | About 6.28773 Money | 0.0117647 Velocity/s |
| Larger Monitor | About 6.28773 Money | 0.0117647 Velocity/s |

The three hardware records really do share the same raw cost and rate. Their availability and effective boosts differ; their final displayed prices are not necessarily identical. [D/C]

### Funding rounds

| Round | Required Lines | Base Money reward before upgrades |
|---|---:|---:|
| Seed | 3,600 | 1,000 |
| Series A | 300,000 | 88,000 |
| Series B | 88,013,432 | 25,865,000 |
| Series C | `1.29057e13` | `3.93133e12` |
| IPO | `2.67075e24` | `8.50405e23`; completes the chapter |

A higher series must be unlocked, and the preceding series must have been completed. IPO is explicitly manual: it does **not** autocomplete like ordinary funding rounds. [D/C]

### Upgrade progression

These are the recovered named upgrades and their role. Their arrangement below follows the broad progression, not an asserted cheapest-first purchase order.

| Group | Upgrades and effects |
|---|---|
| Initial production | Clipboard History: 5x copy/paste. Notepad: start each round with one Variable and Function. Documentation: triple all yields. Autocomplete: 2x Lines. |
| Surplus fundraising | Persuasion: extra Money for surplus Lines, described as 25% with a penalty on large surplus. Marketing increases the surplus conversion to 50%. |
| Generator access | Better Hardware adds a generator tier; IDE another line generator; Books the final tier-one generator. Multi-Monitor unlocks the last tier-two generator. |
| Local upgrade access | Organization unlocks Variable/Function upgrades that reset with rounds. Moore's Law adds a velocity upgrade. Improvements unlocks two more upgrades. |
| Costs and count bonuses | Cost Cutting: 90% cheaper generators. The More You Buy: line-generator yield bonus based on owned count. Ergonomics: velocity-generator cost reduction compounded with owned velocity generators. |
| Autobuy | Dictionary buys Variables/Functions; Templates buys Classes; Syntactic Sugar buys Patterns. |
| Tap boosts | Longer Clipboard History: 4x; The Key: 5x; Text Expansion: 5x; Sticky Keys: 2x; Keyboard Macros: 10x. Fast Typing: 100x copy/paste and 10x Lines. |
| Line boosts | Delegation: 10x Lines and 2x copy/paste. Bare Metal: 4x Lines. Meta Programming: 4x Lines. Activist Investors: a further small line boost. |
| Round income | More Investors: 2x Money. Even More Investors: 3x. Money: 10x. Insiders: another 10x. |
| Velocity on restart | Memory: seven hours of production. Review: twelve hours. Retrospectives: another twelve hours. More Retrospectives: another 24 hours. Analysis: another 24 hours when starting a round. |
| Reset improvement | Institutionalized Knowledge prevents the top two local upgrade tracks from resetting. |
| Automation | Continuous Integration automates eligible round completion. Work From Home simulates offline rounds using the recorded fastest round, for 10% of normal Money. Home Office doubles that yield; Lighting doubles it again. |
| Series access | Series A, Series B, Series C, IPO unlock the corresponding selectable round subject to the prior-round requirement. |

The local upgrade labels also include a build track (**Build Scripts, Make File, Build Pipeline, Build Environment, Containerization**) and a testing track (**Manual Testing, Test Cases, Macros, Automated Testing**). These are not additional independent prestige currencies. [D]

### Reset contract and automation

A funding round awards Money, removes current/cumulative Lines and Velocity, and removes the four line-generator purchases. Velocity-generator purchases survive. The two local upgrade entries reset unless Institutionalized Knowledge is owned. Durable main upgrades, Money, round access, and completed-round information survive. Notepad seeds the first two line generators; the velocity gifts are computed from retained production and applied to the new run. [C]

Consequently, "seven hours of free velocity" means a production grant calculated from the current velocity-generating infrastructure, not a universal fixed amount of Velocity. Buying more retained hardware improves future grants. [C/I]

Autobuy exposes a **payback-time threshold**. Work From Home uses the displayed fastest round time; switching round types resets the record. Do not assume it uses your best historical time across every funding series. [D]

### Progression loop [I]

1. Repeat Seed rounds to buy permanent Money upgrades and velocity hardware.
2. Invest in the upgrades that shorten restart time: initial generators, retained upgrades, and instant Velocity grants.
3. Unlock and complete each series, then compare its Money reward against its cycle time rather than assuming the latest series is always best.
4. Tune autobuy so it does not consume the Lines needed for an imminent round completion without paying itself back.
5. Record a fast run of the series you intend to idle with before relying on Work From Home.
6. Unlock IPO after Series C, reach the IPO Line requirement, and complete it manually.

### Story

**Just Getting Started** establishes the startup and investor Steelman. **Going For Gold** develops the fundraising story after the early financing progression. **Bureaucracy** brings the business into public-company constraints. [D]

## Chapter 5: Recurse, Recurse, Recurse

### Economy and recursion

**Power** represents search capability. Producing it also accumulates **Tech Debt**. **Knowledge**, **Serendipity**, and **Features** are the three local prestige resources. The production ladder is recursive: the second tier makes first-tier generators, and the third tier makes second-tier generators. [D/C]

| Generator | Base Power price | Base production |
|---|---:|---|
| Querying | 4.64918 | 0.43 Power/s and `0.43*exp(-8)` Tech Debt/s |
| Retrieval | 23.8075 | 0.56 Power/s and `0.56*exp(-8)` Tech Debt/s |
| Presentation | 86.4875 | 0.62 Power/s and `0.62*exp(-8)` Tech Debt/s |
| Scoring | 19,148.9 | One bonus Querying every 37.4 s |
| Ranking | 1.02336e6 | One bonus Retrieval every 38.9 s |
| Tokenization | 4.12852e8 | One bonus Presentation every 40.2 s |
| Document Indexing | 2.55163e9 | One bonus Scoring every 1,260 s |
| Inverted Index | 1.58431e10 | One bonus Ranking every 1,285 s |
| Analysis | 1.05925e11 | One bonus Tokenization every 1,320 s |

Tech Debt is **subtracted from each basic generator's per-second yield before stat boosts**. It is not merely a percentage penalty on total Power. This makes debt disproportionately harmful to weak starting generators and explains the need for repeated resets and debt reduction. [D/C]

Generated copies and manually purchased copies are distinct. Economies Of Scale explicitly uses the manually bought count, so an enormous generated population is not interchangeable with buying that many copies. [D/C]

Serendipity's recovered debt factor is `min(1, 1/Serendipity)`, with a missing balance returning one. Above one Serendipity, more of it suppresses debt through this reciprocal factor. Knowledge primarily finances the upgrade system, while Features finance later Planning levels and supply the chapter's completion balance. [C]

### Upgrades and unlocks

| Upgrade | Recovered schedule / effect |
|---|---|
| Micromanagement | Costs 1 Knowledge, then about 2.97994e7 Knowledge; opens generator upgrade systems. |
| Cloud Computing | Knowledge costs about 3,714.50; 202,805; 3.00989e7; 1.21428e10. Each level reduces Tech Debt by 90%. |
| Economies Of Scale | About 228,662 Serendipity; rewards manually purchased generator counts with yield growth. |
| Planning | Costs about 2.50111e9 and 1.00902e12 Serendipity, then 26.5758 and 79,221.3 Features. Reorg starts generator upgrades at increasing baseline levels rather than zero. |
| Basic-generator upgrades | Yields, Cost, Tech Debt. Yield improvements also affect their debt-producing output; cost levels reduce price by 90%; debt upgrades leave 25% as much debt production (a 75% reduction). |
| Recursive-generator upgrades | Yields, Speed, Cost. Raising production here grows the lower tier over time. |

Planning's reset code seeds upgrade levels in increments of five per Planning level. It does not simply grant five physical copies of every generator. [C]

| Quest | Restriction | Target Power | Reward |
|---|---|---:|---|
| Legacy Systems | Start with 0.1 Tech Debt | 18,215 | Debt accumulation halved. |
| Game Jam | No Tech Debt, but a one-minute run/reset constraint | 1,540.71 | Unlock generators that produce generators. |
| Game Jam 2 | Same challenge family | 1.97764e7 | Unlock generators that produce generator-generators. |
| Direct To Consumer | Recursive generators produce Power instead of their normal generators | 3.42542e15 | 10x speed for the recursive generator layer. |

The appendix retains the exact recovered wording of these quests. A Game Jam is a short challenge that changes the economy; it is not permission to ignore debt in the ordinary run. [D/I]

### Three reset layers

Prestige gains use cumulative Power: [C]

| Layer | Award formula |
|---|---|
| Knowledge reset | `Power * exp(-6.42)` = about `Power * 0.001628656` |
| Reorg | `Power * exp(-8.42)` = about `Power * 0.0002204147` Serendipity |
| Deploy | `Power * exp(-47.71)` = about `Power * 1.904628e-21` Features |

| Action | Cleared | Preserved |
|---|---|---|
| Knowledge reset | Purchased generators at all three tiers; Power current/cumulative; Tech Debt; generated copies of the first two tiers; bar progress | Adds Knowledge; keeps existing Knowledge, Serendipity, Features, upgrades, and quest completions. Active Legacy Systems reseeds its debt. |
| Reorg | The same production layer, plus Knowledge and generator upgrade levels | Adds Serendipity; keeps Features, global unlocks, and quest completions. Planning seeds generator upgrade levels. |
| Deploy | Production layer, Knowledge, Serendipity, generator upgrades, global upgrade entries 1-5, **Legacy Systems and Game Jam completion records** | Adds Features; **Direct To Consumer completion survives**. |

Deploy is therefore a substantial rebuild: the Game Jam unlock challenges must be redone. It is not just a Reorg that happens to award a different currency. This distinction is established by quest IDs in the reset routine and their construction sites. [C]

### Progression loop [I]

1. Build Power only while debt permits useful growth; bank Knowledge instead of trying to wait indefinitely through a debt wall.
2. Reduce debt and complete Legacy Systems. Use the first Game Jam to gain the second production tier.
3. Obtain Serendipity and Planning so deeper resets rebuild generator upgrades faster.
4. Complete Game Jam 2 and let third-tier production grow the second tier, which then grows the first.
5. Complete Direct To Consumer for its persistent acceleration before relying heavily on Deploy cycles.
6. Deploy for Features, rebuild the two cleared challenge families, and repeat toward about `8.71080e11` Features.

### Story

**The Right Idea** discusses the technical-debt problem and the rewrite approach. **Open Source** turns the project toward opening its code, which becomes the central business and licensing conflict of the following chapters. [D]

## Chapter 6: Free As In Beer

### Economy

The project now revolves around **Issues**, **Commits**, and **Velocity**. The main Git progress bar is the commit-production bottleneck. **Integration** from Rebase boosts Velocity; **Awareness** from Conference boosts Issues. A further tier creates bonus issue generators. [D/C]

| Family | Generator | Base Commit price | Base output |
|---|---|---:|---:|
| Velocity | Git Status | 1,008.25 | 1.37713 Velocity/s |
| Velocity | Git Add | 1,993.39 | 1.58407 Velocity/s |
| Velocity | Git Commit | 2.56893e6 | 1.85893 Velocity/s |
| Velocity | Git Push | 5.55384e6 | 2.18147 Velocity/s |
| Velocity | Git Pull | 5.86686e8 | 2.38214 Velocity/s |
| Issues | Feature Idea | 1,993.39 | 2.27050 Issues/s |
| Issues | Bug Report | 31,539.6 | 2.80107 Issues/s |
| Issues | Documentation | 186,461 | 3.12677 Issues/s |
| Issues | UX Design | 2.47067e6 | 3.42123 Issues/s |
| Bonus copies | Requirements Definitions | 1.67165e14 | One Feature Idea per 313.8 s |
| Bonus copies | Test Case Creation | 3.13870e14 | One Bug Report per 307.8 s |
| Bonus copies | API Design | 6.68445e14 | One Documentation per 298.2 s |
| Bonus copies | Wireframe Refinement | 4.20857e15 | One UX Design per 277.2 s |

The main bar, labeled **git init**, has a 15.6-second base cycle, consumes **one Issue**, and produces **one Commit**. Its separate base purchase/upgrade cost is about **230.973 Issues**. Velocity accelerates the rate at which Issues become Commits. The cycle-input and purchase-price fields are separate; 230.973 is not the base per-cycle consumption. [D/C]

### Upgrades and challenges

| Upgrade | Commit costs | Effect |
|---|---:|---|
| Faster Issue Creation | 81,633.9; 994,505; 3.25105e12 | Opens more issue-generator options. |
| Collaboration | 2.97994e7; 8.90608e9 | Issue speed +50% per described level. |
| QA Tools | 8.88309e10; 1.07939e13 | Bonus issue-generator support, including restart seeding. |
| Wiki | 2.64117e15 | All generator costs reduced by 90%. |

| Challenge | Restriction and targets | Reward |
|---|---|---|
| Bug Bash | No special restriction; `3.90973e18` Velocity | Autobuy for Issue and Velocity generators. |
| Internal Review 1-3 | Costs 100x; three-minute limit; `9.22488e7`, `6.13585e10`, `2.40762e16` Commits | Issue-yield bonus based on manually bought issue-generator count, with coefficients 1%, 2%, 3%. |
| Triage 1-2 | Commit production disabled; `4.47559e16`, `1.33416e20` Issues | 10x improvement to the upper generator layer per stage. |

### Two prestiges

- **Rebase:** `Integration gain = cumulative Commits * exp(-16.9)`, about `4.57534e-8` times cumulative Commits.
- **Conference:** `Awareness gain = cumulative Issues * exp(-11.22)`, about `1.34034e-5` times cumulative Issues.
- Each Integration increases Velocity by 1%; each Awareness increases Issues by 1%. [C/D]

Both resets clear current/cumulative Commits, Issues, and Velocity. They reset the main/ordinary production purchases and the first two upgrade tracks. The code removes generator IDs 1, 11-15, and 1001-1004; the upper generator IDs 1051-1054 are not removed by those lists. Integration and Awareness survive each other's reset, as do QA Tools, Wiki, and completed challenges. QA Tools rebuilds the appropriate bonus issue-generator state. [C]

This makes Rebase and Conference **parallel persistent investments**, not two strictly nested reset depths. Use whichever resource is currently limiting the next run. [I]

### Progression loop [I]

1. Develop issue supply and commit throughput together; a one-sided investment can leave the other side limiting output.
2. Rebase for Integration and Conference for Awareness. Their persistent gains reinforce different halves of the loop.
3. Unlock upper generators and QA Tools so repeated resets do not restart the issue economy from nothing.
4. Complete Internal Reviews when you can meet the time limit despite 100x costs.
5. Use Triage's issue-only run to strengthen the recursive layer, and Bug Bash to unlock purchasing automation.
6. Push to about `6.27220e22` Commits.

### Story

**RJS** introduces the licensing warning as the open-source argument becomes concrete. **Rollback** follows Bicho's use of the released code: the prior release's licensing cannot simply be wished away by changing the current business plan. The story proposes returning to an earlier code state and rebuilding the product. These are the fictional game's story beats, not legal advice about real licenses. [D]

## Chapter 7: TL;DR

### Economy: three kinds of debt

This is a redesigned Power economy with **Bugs**, **Documentation Deficit**, and **Performance Issues** tracked separately. **Knowledge** is the Rewrite resource. **Efficiency** is produced by a clean second tier and boosts Power production, while other upgrades reduce its adverse interaction with debt. [D/C]

| Generator | Base Power price | Base output |
|---|---:|---|
| Rapid Development | 4.64918 | 0.43 Power/s; `0.43*exp(-8)` Bugs/s |
| Code Changes | 354.249 | 0.56 Power/s; `0.56*exp(-10)` Documentation Deficit/s |
| Advanced Algorithms | 1,422.26 | 0.62 Power/s; `0.62*exp(-12)` Performance Issues/s |
| Resources | 278,366 | 0.13 Efficiency/s |
| Innovation | 2.12104e7 | 0.21 Efficiency/s |
| Development | 1.26383e10 | 0.32 Efficiency/s |
| Test Lab | 1.55112e12 | 0.46 Efficiency/s |
| Server Farm | 1.02409e14 | 0.62 Efficiency/s |

Initially, effective Tech Debt is the **sum** of its three components. **Compartmentalization** changes it to their **maximum**. The resulting value is subtracted from each basic generator's per-second output before stat boosts. It is not subtracted once from the chapter's grand total. The Efficiency producers form the route around the debt-limited first tier. [D/C]

### Upgrades

| Upgrade | Effect |
|---|---|
| Pair Programming | Reduces Bugs by 90%. |
| Templates | Reduces Documentation Deficit by 90%. |
| APIs | Reduces Performance Issues by 90%. |
| Compartmentalization | Debt becomes the maximum component instead of their sum. |
| Not Like That | Reduces Efficiency's effect on debt by 50%. |
| Protocols | Doubles tier-one yields without doubling debt. |

These name/effect pairs were checked against the associated description closures rather than inferred from the order of strings in the binary. Knowledge-denominated level schedules are in the data appendix. [C]

### Challenges and Rewrite

**Last Minute Optimizations** has five stages. Each halves Efficiency boosts during the challenge. Rewards double Efficiency's effect, reduce debt, and seed clean generators after Rewrite: first Resources, then Resources+Innovation, then Development, then Test Lab, then Server Farm. [D]

| Stage | Power target |
|---|---:|
| 1 | `2.19129e6` |
| 2 | `3.96194e9` |
| 3 | `3.56643e11` |
| 4 | `1.30525e13` |
| 5 | `1.43880e14` |

**No Comments Ever**, two stages, makes generators that do not already create Documentation Deficit add it. Targets are about `7.34845e6` and `3.25105e12` Power. Each reward reduces Efficiency's debt influence by 90%. [D]

Rewrite awards `cumulative Power * exp(-6.42)` Knowledge. It clears purchased generators, Power current/cumulative, all three debts, Efficiency, and transient progress. Knowledge, upgrades, and completed challenges survive. It then seeds the Efficiency-generator copies earned from Last Minute Optimizations. [C]

### Progression loop [I]

1. Buy the appropriate debt reduction for the component actually restricting output, rather than treating every kind of debt as one indistinguishable number.
2. Establish Knowledge through Rewrite and open the Efficiency tier.
3. Compartmentalization prevents the three debt tracks from simply adding together; it does not remove the largest debt component.
4. Complete early Last Minute Optimizations stages to make future restarts begin with clean production.
5. Stack Not Like That, No Comments Ever, and the challenge rewards to separate productive Efficiency growth from debt growth.
6. Use Protocols and the mature clean layer to reach about `7.05844e15` Power.

### Story

**Not My Problem** continues the dispute with Bicho over the fork and its license. **Progress** develops the cleaner design; **Takeover** turns the conflict toward control of the companies themselves. [D]

## Chapter 8: Glory To The Coming Age Of Free Software

### Economy

**Money** buys machines. Machines accumulate **Assets**. **Liquidate** converts Assets into Money. **Productivity** boosts Asset generation by 1% per point. **Buy Shares** is a deeper reset that converts Money into **Shares**. A late opposing counter tracks **Bicho's Shares**, which can trigger a forced restart. [D/C]

| Generator | Base Money price | Base output |
|---|---:|---|
| Personal Computer | 3.59664 | 0.52 Assets/s |
| Computer Network | 208.513 | 1.58 Assets/s |
| Cloud Cache | 2,489.91 | 5.32 Assets/s |
| Server Farm | 11,614.4 | 12.08 Assets/s |
| Meetings | 4,105.16 | About 0.0308074 Productivity/s |
| Breaks | 2.87051e6 | 0.26 Productivity/s |
| Organisation | 2.31479e8 | 0.82 Productivity/s |
| Deadlines | 7.72258e10 | 2.76 Productivity/s |
| Components | 1.28207e8 | One bonus PC per 1.21 s |
| Switches | 2.32600e10 | 2.32 bonus Networks per 1.42 s |
| RAM | 2.53848e11 | 6.85 bonus Caches per 1.84 s |
| Motherboards | 2.55737e12 | 18.85 bonus Servers per 1.33 s |

The late Bicho generator has a base one-share-per-second record and an effectively unbuyable placeholder price. It is an internal opposing mechanism, not another normal machine for the player to purchase. Its effective rate is modified by progression and Poison Pill. [D/C]

### Liquidation and share formulas

Starting Money is **10**, or **1e10** when Market Power is owned. The recovered liquidation potential is based on 80% of Assets, modified by logarithmic Money/Share bonuses and a starting-Money floor. The balance assignment is more specific than the UI's generic reset text: [C]

`Money after liquidation = max(10, current Money + liquidation potential - 10)`

Thus Liquidate does not simply discard unspent Money and replace it with the displayed reward.

Before Bulk Buy, at least **10,000 Money** permits buying **one Share** in a share reset. With Bulk Buy, the recovered gain is:

`Shares gained = (Money / 10,000)^q`, with `q = 0.20 + 0.01*min(Market Manipulation level, 6)`.

Below 10,000 Money the result is zero. The routine returns a `CustomNum`; it does not explicitly floor the result in this function. The ordinary meaning of whole stock-market shares should not be used to invent integer rounding. The level cap in the formula is not evidence that six purchasable Market Manipulation levels exist in this build. [C]

### Upgrades

| Upgrade | Currency / notable costs | Effect |
|---|---|---|
| Discipline | 5 Shares | Unlocks Productivity generators. |
| Metrics | About 250.385 Shares | Unlocks Productivity upgrades. |
| Bulk Buy | About 2.57150e6 Productivity | Enables buying multiple Shares in a reset using the power-law formula. |
| Market Manipulation | Shares: 92.7586; 83,283.0; 1.23923e6; 9.60136e7 | Improves the bulk-share exponent / reduces additional-share cost. |
| Economies Of Scale | Money: 1.53966e11; 5.08550e13; 1.62003e30; 7.85984e38 | Manually bought tier-one count increases yields. |
| Metrication | About 9.19480e16 Money | Allows owning both Discipline and Metrics. |
| Market Power | Shares: 2.57150e6; 9.43560e6; 3.82632e7; 7.68538e8 | Raises starting Money to 1e10 and supplies passive Money from liquidation potential. |
| Poison Pill | Shares: 28,566.8; 384,616; 4.23969e6; 6.30854e7; 1.97253e8 | Slows Bicho's share accumulation. |
| Snowballing Productivity | Productivity: 4.20693e9; 1.19909e11; 5.88004e11; 8.72676e13 | Manually bought Productivity-generator count improves total yields. |
| Compound Interest | Money: 1.81371e25; 4.80435e35; 8.20178e37 | Logarithmic current-Money bonus to liquidation income. |
| Benefits Program | About 5.69347e8 Shares | Logarithmic current-Share bonus to liquidation income. |

Discipline and Metrics initially constitute a choice; Metrication removes that exclusivity. Market Power descriptions include passive rates of **0.001%, 0.002%, 0.004%, 0.008%, 0.016%** of liquidation potential per second. Only four literal purchase prices were recovered in this build's list. The presence of a fifth description branch is not enough to claim a fifth purchasable level. [D/C/U]

No Chapter 8 QuestLevel challenge family was found. Its special challenge is the opposing Bicho counter. [C]

### Exactly what the three resets do

| Action | Cleared / replaced | Preserved |
|---|---|---|
| Liquidate | Assets; bought PC/Network/Cache/Server generators; bonus copies of those four; their cached boosts; bar progress. Money is updated by the formula above. | Productivity and its generators; upper bonus-producing generators; Shares; all upgrades; Bicho's accumulated counter. |
| Buy Shares | Money replaced with starting Money; Assets; Productivity; all three ordinary generator tiers; bonus asset-generator copies; Discipline and Metrics; transient progress | Adds Shares. Keeps higher upgrades and Bicho's counter. |
| Bicho reaches 50 million Shares | Clears current stat balances, purchased-generator map, cached boosts, upgrade map, and bar progress; seeds a specific upgrade preset | Does not perform a full global or other-chapter reset. Global token progress remains. Cumulative/statistical history is not explicitly cleared by this routine. |

The Bicho preset is: **Discipline 1, Metrics 1, Bulk Buy 1, Market Manipulation 2, Economies Of Scale 2, Metrication 1, Market Power 1**. Starting Money is calculated before the upgrade map is cleared, which normally gives the Market Power starting balance at this point in progression. [C]

The UI says it resets progress to the point before Bicho starts accumulating. The implementation uses a **fixed preset**, not an exact snapshot of your previous generator counts, Money, or purchased upgrades. [D/C]

### Progression loop [I]

1. Buy the asset machines, accumulate Assets, and Liquidate to finance a larger machine base.
2. Start buying Shares and choose the early Productivity path. Remember that buying Shares clears Productivity while Liquidate preserves it.
3. Obtain Bulk Buy and Market Manipulation so a single share reset can produce substantial progress.
4. Use Metrication to combine the previously exclusive paths. Establish the bonus-generator layer and count-based boosts.
5. Market Power reduces dependence on manual liquidation, while logarithmic Money/Share bonuses reward larger balances.
6. Once Bicho starts accumulating, evaluate Poison Pill purchases against the lost Share balance and the extra time gained. A high Share count alone is not enough if the opposing counter reaches its threshold first.
7. Reach roughly **five billion Shares** for the Chapter 8 completion target. Bicho's **50 million** target belongs to the opposing company/counter; it is not your completion requirement.

### Story and ending

**Stonks** frames the hostile-takeover plan around ten billion shares and obtaining control. **Turnabout Is Fair Play** has Bicho retaliate by acquiring shares in the player's company: fifty million out of one hundred million is the competing takeover threat. [D]

**Class A** supplies the reversal: economic ownership is not equivalent to voting control. Steelman's Class A voting position defeats the apparent victory; the CEOs are removed, and Taffy replaces Bicho. This is the Chapter 8 ending, not an additional Share-production rule. [D]

## Coverage, uncertainties, and source map

### What is established

All eight named chapter implementations were located. The analysis recovered the currency families, main generator chains, named upgrade families, available challenge records, chapter targets, prestige gain constants, and reset mutations. The most important distinctions are backed by specific code paths: cumulative versus current resources; purchased versus bonus generators; parallel versus nested prestige; and the fixed Bicho reset preset.

### What remains unresolved

This reference deliberately does not invent:

1. Every exact **UI reveal threshold** for every section. An object existing in the chapter does not prove it is visible from the start.
2. Every **runtime-generated upgrade cost and multiplier**, especially Chapter 3's interdependent divisions and Chapter 4's local upgrade tracks. Literal schedules are supplied where decoded.
3. An experimentally tested reconciliation of Chapter 3's Scripting reset description with the code.
4. Every interaction among boost order, automatic purchases, and partial progress bars during the same tick.
5. All **quest entry, cancellation, timeout, and resume edge cases**, or the full global offline simulation contract.
6. Your device's remote overrides, difficulty selections, token assignments, purchases, or save state. APKs contain the program, not those live values.
7. An experimentally verified fastest route or exact completion times. The walkthroughs explain the progression implied by the recovered rules.

These gaps limit the word "complete": this is a substantial chapter-by-chapter reconstruction with auditable evidence, not recovered original source or a validated executable simulator.

### Source map

In the evidence archive, `annotated-assembly/ChapterNGameState.dart` is the primary source for chapter N. `resolved-analysis/ChapterN.txt` adds human-readable enum and numerical annotations. The original object/pool dumps provide cross-checks for those annotations.

| Subject | Named source / representative address |
|---|---|
| Chapter 1 prices and resets | `getCostToBuyOneMoreGenerator`, `getUnlockCost`; `prestige1` 0x6aab84; `prestige2` 0x6a9830; `knowledgeOnRewrite` 0x6aadc0 |
| Chapter 2 Promotion | `_prestige1` around 0x6adf8c; `experienceOnPromotion`; `getCostToBuyOneMoreProgressBar` 0x6aecf0 region |
| Chapter 3 prestige / softcap | `applyNetworkingPrestige`, `applyStoragePrestige`, `applyScriptingPrestige`; `applyPrestigePointPenalty` 0x6b21b8 |
| Chapter 4 funding | `prestige`, `completePrestigeDontReset`, `moneyOnPrestige`, `basePrestigeYields`, `linesRequiredForPrestige` 0x54b3b8 region |
| Chapter 5 reset hierarchy | `applyPrestige1/2/3`, `prestigePoints1/2/3`; Deploy mutations at 0x6bac4c onward |
| Chapter 6 dual prestige | `applyRebase`, `applyConference`, generator initialization and quest construction |
| Chapter 7 effects | `ensureInitialized` 0x506b08; description closures 0x507c88-0x507cc4; Rewrite implementation |
| Chapter 8 reset distinction | `bichoReset` 0x54ca28; `sharesCanBuy` 0x6c4a40; `quadraticBase` 0x6c4ba8; `applyLiquidate` 0x6c4cd0; `applyBuyShares` |
| Tokens | `CoreData.recomputeTokenBoost` 0x4802xx region; `addCompletionToken` 0x694e6c; `IPlayableGameState` completion caller |
| Numerical representation | `custom_num.dart`; `root` 0x54b18c multiplies the stored logarithm by its argument, meaning exponentiation |
| Story | `StoryPoint` objects; full selected dialogue in the separate transcript |

All APK byte lengths and SHA-256 hashes are included in the structured data and evidence archive so the analysis can be tied to this exact uploaded build.
