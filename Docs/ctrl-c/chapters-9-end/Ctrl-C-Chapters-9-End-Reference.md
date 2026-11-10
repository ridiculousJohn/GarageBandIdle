# Ctrl C: Chapters 9 to the end

## Scope and how to read this reference

**This APK contains playable Chapters 9-15. Chapters 16 and 17 are named placeholders.** Chapter 15 ends on a story cliffhanger; there is no recovered playable conclusion after it in this build.

This is a separate companion to the Chapters 1-8 reference. It uses the same four uploaded APKs: package `com.amorfatite.pretentious`, version **1.12.2**, build **146**. Assets and Android resources came from `base.apk`; most gameplay rules came from the compiled Flutter/Dart program in the ARM64 split. The other splits supply language/density resources. Nothing here relies on a public wiki or a different game version.

### Evidence labels

- **[D] Direct data:** literal names, descriptions, dialogue, generator records, and numerical constants recovered from the APKs. A description proves what the game says, not necessarily what its code does.
- **[C] Code-derived:** behavior reconstructed from the compiled program, including reset writes and formula operations. This is static analysis, not an executed playthrough.
- **[I] Inference:** suggested progression based on those mechanics. These routes are not claims of optimal speed or developer-confirmed intent.
- **[U] Unresolved:** a specific detail that this reconstruction does not establish. These are called out instead of supplying a plausible invented rule.

Numbers are usually **approximate**. The game's large-number type stores logarithms, and some recovered listings round those logarithms. Seven significant digits in a table do not imply seven-digit certainty in the original design value. `exp(x)` means the underlying value is e raised to x. All generator tables show **raw, unboosted values**, before purchased quantity, bonus generators, chapter upgrades, completion-token effects, and difficulty adjustments.

### Companion files

- [Data appendix](Ctrl-C-Chapters-9-End-Data-Appendix.html): every recovered generator record, literal upgrade price schedule, original upgrade description, and constant quest record for these chapters.
- [Story transcript](Ctrl-C-Chapters-9-End-Story-Transcript.html): all 16 remaining story records, including the empty scene.
- `Ctrl-C-Chapters-9-End-Recovered-Data.json`: structured records for further analysis.
- `Ctrl-C-Chapters-9-End-Evidence.zip`: annotated disassembly, resolved chapter listings, object/pool listings, and APK hashes. The `.dart` files in that archive are annotated ARM64 disassembly, not original Dart source.

### Chapter map

| Chapter | Main progression | Completion requirement in this build |
|---|---|---|
| 9 - Tivoization | Lines -> Knowledge -> Data -> Career Capital | Approximately 1.305249e13 current Career Capital |
| 10 - LNU/Leinix | Recursive utility toolchain; kernel versions and Refactoring | Final kernel upgrade, requiring approximately 1.627653e63 current Utility |
| 11 - Or Any Later Version | Associates -> Silver -> Gold -> Titanium membership | Approximately 1.305249e13 current Titanium Members |
| 12 - The Great Reset | Research -> Discovery; generators that produce other generators | Approximately 1.073308e50 current Research |
| 13 - Free As In Anarchy | Buy service stats; multiply them into Power; research and Sprints | Approximately 1.380871e115 current Power |
| 14 - Return Of The Mach | Compile -> Navigation/I/O -> Ops/Throughput -> Stability -> Functionality | Approximately 137760.3 current Functionality |
| 15 - We Don't Break User Space | Symbols -> Pull Requests; promotion -> Stars -> Hype | Approximately 7.199113e24 current Hype |
| 16 - You'll Own Nothing And You'll Be Happy | Placeholder | No recovered completion condition |
| 17 - Year Of Leinix On The Desktop | Placeholder | No recovered completion condition |

The ordinary completion buttons read the **current stat map**, not the lifetime-earned map. Spending the target currency can therefore move you below the requirement. Chapter 10 completes through its kernel-upgrade method instead of a normal completion button. [C]

## Shared rules and repeated completions

### Buying and production

The ordinary next-unit price follows `base_cost * 1.3^purchased_count * applicable_cost_modifiers` in Chapters 9-15. Purchased quantity and bonus/generated quantity are separate. A generator that produces bonus generators can improve production without purchasing another copy at the next geometric price. Bulk-buy totals follow the sequence of next-unit prices; the base price in a table is not the price of a later purchase. [C]

Important exceptions are chapter-specific:

- Chapter 10's Monoliths quest links the purchase-count exponent across generators. Alpha Testing imposes a previous-generator ownership requirement.
- Chapter 11's Overhead quest links Engagement-generator costs.
- Chapter 12's upper-tier generators share a purchase-count pool, and Refinement disables them through a special cost path.
- Chapter 13 purchases immediately add service stats; they are not ordinary continuous stat-producing generators.
- Chapter 14 has multiple independent cost reductions. Its Cost Reducer description and generator-price implementation do not have identical scope.
- Chapter 15's Syscall Vector contributes `0.01^level` to generator prices.

A timed generator's listed output is **per completed cycle**. A continuous generator's output is per second per effective unit. Chapter 13's output is **per purchase**. Treating those three kinds alike gives incorrect progression estimates. [C]

### Local prestige versus chapter completion

A local prestige applies that chapter's selective reset routine. The exact reset tables below matter: a higher-looking prestige does not necessarily call every lower prestige. In particular, Chapter 14's two early prestiges are parallel, and Chapter 15's Release leaves the Symbol economy intact. [C]

Completing a chapter invokes the shared chapter-completion path: award the global reward, expose the next chapter/story transition, and reset the completed chapter's local run. Local prestige currencies, local research/version counters, and local upgrade/quest retention are not permanent account rewards merely because they survive a local prestige. Other chapter saves, global completion tokens, and viewed-story history are separate persistent state. [C]

### Completion tokens and replay value

The shared completion-token code is the same for these chapters as for Chapters 1-8:

- A first completion creates that chapter's token entry with **1 token**. [C]
- A repeat adds `1 + 0.1*d + 0.012*d^1.5`, where `d` is the stored chapter difficulty index. At `d = 0`, a repeat adds 1. [C]
- With the bundled default **5% per token** and the uncomplicated assignment configuration, the combined boost has the form `product_over_chapters(1 + 0.05*tokens_for_that_chapter)`. It is not `1.05^total_tokens`. [C]
- The actual boost also passes through token assignment, configurable penalties/overrides, and a preference exponent clamped to 0-1. The bundled default for `NewCompletionTokenSystem` is false. No live remote configuration was retrieved. [D/C]

Consequently, repeat completions help later chapters through the shared boost, while their local currencies do not transfer. The best chapter to repeat depends on your existing token distribution, selected difficulty, and completion time; no fixed fastest farming chapter can be established from static data alone. [I/U]

### Notation for prestige formulas

`earned(X)` means the chapter's accumulated-earned counter for X, as currently maintained by that chapter's code. It is **not necessarily all-time across local prestiges**, and it is not always the displayed current balance. The reset tables specify which earned counters are cleared. This distinction is especially important in Chapters 9 and 15.

## Chapter 9 - Tivoization

### Economy and progression loop

The chapter returns to a coding economy. Mouse and keyboard purchases produce **Lines**. **Rewrite** converts earned Lines into **Knowledge**. Knowledge buys generators that produce **Data**. **New Project** converts earned Data into **Career Capital**. Capital buys retention, automation, and timed generators that manufacture bonus copies of the earlier generators. [D/C]

The UI describes Knowledge as increasing Lines gain by 1% per point, Career Capital as increasing Lines and Data gain by 1% per point, and Data as increasing the speed of gaining Lines. These are separate feedback layers. `Tools` also exists as a stat and is removed by New Project, but no distinct ordinary Tools-producing generator was recovered in this chapter; it should not be turned into an invented fifth production tier. [D/C]

There is an active paste action (`pastedText`). It adds boosted Lines and calls the stat-gained notification. It is a different path from timed generator production. [C]

### Generators

`Generator 1-5` are bonus Mouse/Keyboard/Mechanical/Clickier/RGB counts. `Generator 11-14` are bonus Notes/Files/Code Snippets/Macros counts. The upper tiers produce these counts, not spendable Lines or Data directly. [C]

| ID | Generator | Output timing | Raw output | Base purchase cost | Cycle input |
|---|---|---|---|---|---|
| 1 | Mouse | per second | 1 Lines | 639.0611 Lines | - |
| 2 | Keyboard | per second | 1.2 Lines | 1286.911 Lines | - |
| 3 | Mechanical Keyboard | per second | 1.4 Lines | 2957929 Lines | - |
| 4 | Clickier Keyboard | per second | 1.7 Lines | 5.138361e+09 Lines | - |
| 5 | RGB Keyboard | per second | 2 Lines | 7.606315e+12 Lines | - |
| 11 | Notes | per second | 0.011109 Data | 1.071414e+12 Knowledge | - |
| 12 | Files | per second | 0.01499558 Data | 3.07655e+14 Knowledge | - |
| 13 | Code Snippets | per second | 0.01831564 Data | 1.241169e+17 Knowledge | - |
| 14 | Macros | per second | 0.02237077 Data | 7.071364e+17 Knowledge | - |
| 21 | Wiki | 63.2 s cycle | 1 Generator 1 | 7.508633e+17 Career Capital | - |
| 22 | Discussion Board | 61 s cycle | 1 Generator 2 | 3.169166e+18 Career Capital | - |
| 23 | Video Conferencing | 58.6 s cycle | 1 Generator 3 | 5.007232e+19 Career Capital | - |
| 24 | Knowledge Bank | 55.1 s cycle | 1 Generator 4 | 4.057402e+23 Career Capital | - |
| 25 | Time Tracking | 52.2 s cycle | 1 Generator 5 | 4.449482e+26 Career Capital | - |
| 31 | Binders | 63.2 s cycle | 1 Generator 11 | 2.616174e+21 Career Capital | - |
| 32 | Archives | 61 s cycle | 1 Generator 12 | 3.811252e+24 Career Capital | - |
| 33 | Forum | 58.6 s cycle | 1 Generator 13 | 1.636873e+26 Career Capital | - |
| 34 | Automation | 55.1 s cycle | 1 Generator 14 | 1.694889e+28 Career Capital | - |

The four keyboards are runtime-built objects, so a scan of only constant GeneratorInfo records would miss them. Their raw Lines rates are 1.2, 1.4, 1.7, and 2 respectively. [C]

### Upgrades and automation

The complete price lists are in the data appendix. The following are the functional milestones:

| Upgrade | Recovered role |
|---|---|
| OSHA Standards | Nine levels; each described as another 99% generator-cost reduction. [D] |
| Auction | Seven levels. The award routine raises each applicable bonus count to at least `50 * Auction level`; it does not blindly add that amount every time it runs. Initially applies to the five Lines generators. [C] |
| Interest | Three levels; increases rate per owned generator. [D] |
| Bonus | Eight levels; multiplies generator yields. [D] |
| Compliance Officers | Level 1 sets OSHA Standards to 7 and Auction to 2, preserves those first-tier upgrades, and increases yields. Level 2 preserves the second-tier upgrades on New Project. Costs about 30.5694 and 9136.20 Capital. [D/C] |
| Data Warehouse | About 492.749 Capital; extends Auction's bonus floor to the four Data generators. [D/C] |
| Mechanics | About 442413 Capital; autobuys Lines generators and increases yields. [D] |
| Effective Management | Three levels, paid in Knowledge; adds a Lines-dependent factor to Capital gain. [C] |
| N-Key Rollover | Two levels, paid in Capital; descriptions specify a fourfold speed increase for progress generators. [D] |

The Appendix preserves dynamic description fragments for Interest/Bonus. Their precise full multiplier curves are not asserted here; they depend on level and other upgrades. [U]

### Prestige formulas and exact reset scope

**Rewrite:** `Knowledge gain ~= earned(Lines) * 0.000219425015`. [C]

**New Project:** without Effective Management, `Capital gain ~= earned(Data) * 0.0000144473435`. With Effective Management level `m`, multiply this by `1 + earned(Lines) * exp(m - 35.6)`. [C]

| Action | Resets or replaces | Preserves / awards |
|---|---|---|
| Rewrite | Current and earned Lines; current Data; purchased Mouse through RGB; their five current bonus-count stats. Removes OSHA Standards and Auction unless Compliance Officers exists. | Adds Knowledge to current and earned Knowledge. Preserves Knowledge purchases, earned Data, Capital, upper-tier purchases, and other upgrade families. Reapplies Auction's bonus floor after clearing first-tier bonuses. |
| New Project | Adds Capital first, then zeroes current/earned Knowledge and Data; removes current Tools; removes purchased Notes through Macros. Removes Interest and Bonus unless Compliance Officers level is at least 2. Clears the four Data bonus-count stats. Then calls Rewrite. | Preserves Capital and Capital-funded upper-tier generators/upgrades. The nested Rewrite can award fresh Knowledge from the Lines earned before this reset. Auction/Data Warehouse can reseed cleared bonus counts. |

These statements follow the actual writes in `prestige1` and `prestige2`. A particularly non-obvious consequence is that **Rewrite removes current Data but leaves earned Data available to New Project**. New Project is also not guaranteed to finish with zero Knowledge, because it calls the Knowledge-awarding Rewrite routine after zeroing Knowledge. [C]

There is no recovered quest series for Chapter 9. [D]

### Walkthrough [I]

1. Build Lines production and take OSHA discounts; compare the next upgrade against the next generator purchase rather than comparing base prices.
2. Rewrite for Knowledge, then fund Notes and the other Data producers. Multiple Rewrites can improve the coding loop while the earned-Data counter continues toward a valuable New Project.
3. Use early Capital for Compliance Officers and Data Warehouse. Those change rebuilding cost and retained progress, making subsequent Projects much less manual.
4. Add Mechanics, Interest/Bonus levels, and Effective Management. The latter makes strong Lines runs improve Capital conversion as well as Knowledge.
5. Buy the Capital-funded bonus-generator tiers as they become practical, and use N-Key Rollover to accelerate their cycles.
6. Accumulate approximately **1.305249e13 current Capital** and complete the chapter. Spending Capital immediately before completion can delay the button.

### Story and completion

**I'm Back:** after the hostile takeover, the player returns to the old boss. The boss identifies Steelman as RJS and helps locate Leignis. **Third Act Flashback:** Leignis describes his partnership with RJS, their disagreement over software freedom and Tivoization, and warns that RJS has been acquiring companies. The player resolves to oppose him. [D]

Completion points to scene **20** and Chapter **10**. Evidence: `build` completion target at `0x6c5fcc`; completion call `0x6c61b8`; Rewrite `0x6c6898`; New Project `0x6c6574`; conversion functions `0x6c6b44` and `0x6c6bd4`; Auction `0x50b898`.

## Chapter 10 - LNU/Leinix

### Economy: a recursive software toolchain

**Utility** buys the toolchain. Bash produces Utility; GCC produces **Scripting**; Time produces **Synchronization**; Grep produces **Search**; GRUB produces **Boot**; Readline produces **IO**; Gzip produces **Compression**. The chapter's bonus-generator mapping associates those six support stats with Bash through Readline respectively. This is a recursive production chain: later tools create effective units of earlier tools. [D/C]

**Efficiency** is the local prestige resource. The text calls it a speed increase of 100% per point; the update code applies a factor `1 + Efficiency` to Bash's yield, together with quest rewards. Do not assume it independently multiplies every stage of the recursive chain. [D/C]

### Generators and availability

| ID | Generator | Output timing | Raw output | Base purchase cost | Cycle input |
|---|---|---|---|---|---|
| 1 | Bash | per second | 0.1353353 Utility | 7.84597 Utility | - |
| 2 | GCC | per second | 0.04978707 Scripting | 639.0611 Utility | - |
| 3 | Time | per second | 0.03019738 Synchronization | 24343.01 Utility | - |
| 4 | Grep | per second | 0.01831564 Search | 2.347776e+18 Utility | - |
| 5 | GRUB | per second | 0.011109 Boot | 1.111503e+21 Utility | - |
| 6 | Readline | per second | 0.006737947 Io | 1.336698e+27 Utility | - |
| 7 | Gzip | per second | 0.004086771 Compression | 1.804623e+37 Utility | - |
| 8 | Tar | per second | 0.002478752 Compression | exp(6000.46) Utility | - |
| 100 | Coreutils | per second | 1 Utility | 639.0611 Utility | - |
| 101 | Diffutils | per second | 1 Utility | 639.0611 Utility | - |
| 102 | Findutils | per second | 1 Utility | 639.0611 Utility | - |
| 103 | Inetutils | per second | 1 Utility | 639.0611 Utility | - |
| 104 | Plotutils | per second | 1 Utility | 639.0611 Utility | - |
| 105 | Sysutils | per second | 1 Utility | 639.0611 Utility | - |

Only **Bash through Gzip** are returned by the ordinary display routine. Tar and the six `*utils` records exist in the compiled data, but this routine does not expose them as normal purchases. Tar's `exp(6000.46)` price is not a credible next progression milestone; it is an unused record in this chapter's visible path. [C]

The kernel index `k` starts at 1. Bash and GCC are present initially. Time appears when `k > 1`, Grep when `k > 2`, GRUB when `k > 4`, Readline when `k > 5`, and Gzip when `k > 6`. [C]

### Kernel versions and requirements

The stored table contains 13 labels, beginning with unused index 0's `0.00`. The UI displays the current entry followed by the next label, while its Utility requirement comes from the current entry. [C]

| Kernel index k | Upgrade displayed | Current Utility required, approximately |
|---|---|---|
| 1 | 0.01 -> 0.02 | 10000 |
| 2 | 0.02 -> 0.03 | 3.992787e6 |
| 3 | 0.03 -> 0.10 | 4.255865e20 |
| 4 | 0.10 -> 0.11 | 6.008217e22 |
| 5 | 0.11 -> 0.12 | 7.011961e28 |
| 6 | 0.12 -> 0.95 | 3.318529e35 |
| 7 | 0.95 -> 0.96 | 2.882046e47 |
| 8 | 0.96 -> 0.97 | 1.198110e50 |
| 9 | 0.97 -> 0.98 | 4.275881e53 |
| 10 | 0.98 -> 0.99 | 2.231446e57 |
| 11 | 0.99 -> 1.00 | 1.627653e63; this action completes Chapter 10 |

The table also stores `1.00` with the same final requirement. The completion branch fires at `k >= 11`, before a separate playable `k = 12` run. [C]

### Refactoring and upgrade milestones

- **Pipes:** six levels; boosts each generator according to its non-bonus purchased quantity. Generated copies do not substitute for purchased copies in this particular bonus. [D/C]
- **Refactoring:** seven levels. Unlocks repeated refactors for Efficiency; later levels improve their reward. The first cost is approximately 2.012382e8 Utility. [D]
- **Kernel Modules:** **3.2 Efficiency**; protects Pipes and Refactoring from the kernel/refactor reset. [C]
- **More productivity:** four levels. The minimum Utility for refactoring is the selected refactor-level requirement multiplied by `10^(-5*p)`, with `p` the upgrade level. It lowers the entry requirement, not the final chapter-completion target. [C]
- **Upgrade 1/2/3:** three levels each, paid with the support stats. Original descriptions and prices are in the appendix. Their implementation contains mismatches, described below. [D/C]

Refactoring has a selectable target level, stored in `ch10_refactor_level`. A valid selection is greater than zero and no greater than the current kernel index; otherwise the code uses `max(k - 1, 0)`. Upgrading the kernel removes that preference. [C]

Let `j` be that selected level, `R(j)` its Utility requirement, `U = earned(Utility)`, and `delta = j - max(k - 1, 0)`. Before Refactoring level 2, the recovered reward is `min(U / R(j), 2^delta)`. At level 2 and above the routine adds a level- and logarithm-dependent component, then multiplies by `2^delta`. **The exact arithmetic of that additional component is not fully resolved here**; do not infer the later reward from the first-level formula. [C/U]

### Resets

| Action | Reset scope | Persistent local state |
|---|---|---|
| Upgrade Kernel before final stage | Increments k, then runs kernelReset. Clears current Utility and its earned counter; current Scripting, Synchronization, Search, Boot, IO, Compression; purchased generator IDs 1-8. Removes Pipes and Refactoring unless Kernel Modules exists. | Efficiency, Kernel Modules, More productivity, Upgrade 1/2/3, quest completions, and the advanced kernel index. |
| Refactor | Adds the displayed Efficiency reward, then runs the same kernelReset. | The kernel index stays where it is. The selected refactor target does not rewind the unlocked toolchain. |
| Quest reset | If current Utility meets the refactor minimum, awards the refactor Efficiency first, then runs kernelReset. | Kernel index, Efficiency, protected upgrades, and completed quest records. |

The reset method does not clear every support stat's historical-earned counter, but it clears their current bonus quantities. That residual bookkeeping should not be mistaken for retained productive copies. [C]

### Quests

| Quest | Target | Restriction | Stated reward [D] |
|---|---|---|---|
| Monoliths | 2.889905e+15 Utility | Buying any generator increases the cost of all generators | Doubles utility gain. |
| Monoliths (2) | 6.365439e+19 Utility | Buying any generator increases the cost of all generators | Doubles utility gain. |
| Monoliths (3) | 1.354088e+22 Utility | Buying any generator increases the cost of all generators | Doubles utility gain. |
| Monoliths (4) | 2.982577e+26 Utility | Buying any generator increases the cost of all generators | Doubles utility gain. |
| Monoliths (5) | 2.836149e+30 Utility | Buying any generator increases the cost of all generators | Doubles utility gain. |
| Alpha Testing | 5.802554e+20 Utility | Buying a generator requires at least 70 of the previous generator. | Doubles utility gain. |
| Alpha Testing (2) | 3.809955e+28 Utility | Buying a generator requires at least 80 of the previous generator. | Doubles utility gain. |
| Alpha Testing (3) | 3.712726e+38 Utility | Buying a generator requires at least 90 of the previous generator. | Doubles utility gain. |
| Alpha Testing (4) | 2.262015e+44 Utility | Buying a generator requires at least 100 of the previous generator. | Doubles utility gain. |

Completed quest levels combine as `2^(Monoliths_completed + Alpha_Testing_completed)` in Bash's yield boost. During Monoliths, buying one generator raises the common purchase-cost exponent. During Alpha Testing, the previous-generator requirement rises from 70 to 100 over the four levels. [C]

### Description/code disagreements

The upgrade-description strings are not completely reliable in this chapter. `postUpdateCallback` writes the following generator modifiers:

- Upgrade 1 initially applies about `2.066489e-10` to Grep cost; later it increases Time yield by about 73.6998, then applies another factor e to Time. The third description says Readline instead. [C versus D]
- Upgrade 2 initially applies the same large cost reduction to Gzip; later increases GCC by about 27.1126, then GRUB by e. [C]
- Upgrade 3's first implemented effect increases GRUB yield by about 27.1126, although its description says it reduces GRUB prices. Its next effect boosts Readline by about 27.1126, followed by an e multiplier to Time. [C versus D]

### Walkthrough [I]

1. Buy Bash and GCC, then make the inexpensive early kernel upgrades to extend the recursive chain.
2. Unlock Refactoring and gain enough Efficiency for Kernel Modules. That retention purchase is more consequential than a small immediate generator increase when frequent resets are ahead.
3. Complete affordable Monoliths and Alpha Testing levels. Their doubled Utility output accelerates every subsequent kernel requirement.
4. Use the refactor chooser and More productivity to obtain useful Efficiency before pushing the next large kernel threshold.
5. Spend support currencies on Upgrade 1/2/3 when their implemented effect helps the current bottleneck. Do not budget around their descriptions alone.
6. Push the final 0.99 -> 1.00 action at approximately **1.627653e63 Utility**.

### Story and completion

**Small Poll For My New Operating System** is an actual story record with an empty player dialogue string; there is no missing speech to reconstruct from it. **Team Up** shows young Steelman and young Leignis agreeing to combine the kernel and user-space tools. [D]

Completion calls scene **22** and unlocks Chapter **11**. Key evidence: constructor `0x46db78`; display list `0x6937bc`; kernelReset `0x693ad0`; refactor reward `0x693e20`; minimum `0x69441c`; upgradeKernel `0x694a28`; modifier implementation `0x542e94`.

## Chapter 11 - Or Any Later Version

### Economy and stats

**Recruit Associate** is the main timed action. Engagement increases Associate gain; Velocity increases the recruitment bar's speed. The membership prestiges form **Silver -> Gold -> Titanium**, with each member tier described as adding 1% to Associate gain per point. [D]

Engagement producers cost Associates. Velocity producers cost Silver Members. Documentation, MOOC, and Partnerships cost Gold and manufacture bonus Velocity generators. Titanium funds the final retention and passive-prestige upgrades. [D/C]

### Generators

| ID | Generator | Output timing | Raw output | Base purchase cost | Cycle input |
|---|---|---|---|---|---|
| 1 | Recruit Associate | 3 s cycle | 1 Associates | 639.0611 Associates | - |
| 10 | Outreach | per second | 0.001836305 Engagement | 4146.418 Associates | - |
| 11 | Marketing Campaign | per second | 0.001661557 Engagement | 18033.74 Associates | - |
| 12 | Workshop | per second | 0.004991594 Engagement | 3.575657e+10 Associates | - |
| 13 | Advocacy Group | per second | 0.03337327 Engagement | 9.079811e+17 Associates | - |
| 14 | Hackathon | per second | 0.0450492 Engagement | 6.551625e+21 Associates | - |
| 20 | Accelerator | per second | 0.0007465858 Velocity | 10938.02 Silver Members | - |
| 21 | Certification | per second | 0.002739445 Velocity | 73130.44 Silver Members | - |
| 22 | Onboarding | per second | 0.01227734 Velocity | 1.366024e+07 Silver Members | - |
| 31 | Documentation | 3.8 s cycle | 0.0002248673 Bonus Accelerator | 250196 Gold Members | - |
| 32 | MOOC | 3.2 s cycle | 0.0002248673 Bonus Certification | 1.118406e+07 Gold Members | - |
| 33 | Partnerships | 2.6 s cycle | 0.0002248673 Bonus Onboarding | 7.458238e+08 Gold Members | - |

Recruit Associate is flagged as a special main bar rather than an ordinary purchasable generator. Its raw price field should not be read as a mandatory recruitment unlock fee. The three bonus-output stats identify Accelerator, Certification, and Onboarding respectively. [C]

### Upgrades

| Upgrade | Levels and effect |
|---|---|
| Turbocharge | 3; doubles Associate gains at each step. [D] |
| Flywheel | 4; first automates recruitment; later doubles specified Engagement producers. [D] |
| Boost | 3; tapping recruitment advances the bar by a level-dependent amount. [D] |
| Community | 3; begins autobuying Engagement and Velocity and raises yields. Descriptions progress through 2x, 3x, and 4x yield language. [D] |
| CRM | 3; retains `0.01 * 2^level` of the affected current stat at the appropriate prestige: 2%, 4%, 8%. [C] |
| Cost Cutting | 3; descriptions specify 50%, 90%, and 99% price reductions. [D] |
| Positions | 3; first protects early upgrades, second protects the next upgrade tier, third protects the two quest-completion counters on Gold. [D/C] |
| Auto Promote | 2; passively gains Silver, then Gold, without manually performing the corresponding reset. [C] |
| Expansion | Unlocks third-tier buy options; describes doubled prestige gains. [D] |

All literal costs, including the transitions from Associates to Silver/Gold/Titanium payment, are in the separate appendix.

### Prestige calculations

Let `q` be completed Micromanagement plus Overhead levels. The recovered conversion functions are:

- `Silver gain ~= (earned(Associates) * 0.0804596067)^0.25 * 2^q`.
- `Gold gain ~= (earned(Silver) + currently_available_Silver_gain) * 0.000911881966`.
- `Titanium gain ~= (earned(Gold) + currently_available_Gold_gain) * 0.0000225449379`.

These nested formulas count the lower conversion available in the current run, not just the already banked lower-tier member total. The three conversion methods themselves do not contain a separate Expansion test; its advertised doubling should not be inserted as an extra factor into these formulas without tracing another modifier path. [C/U]

Auto Promote level 1 adds `0.01 * Silver_gain` per second. Level 2 also adds approximately `0.0000006737947 * Gold_gain` per second. Gold promotion is therefore far slower than Silver promotion; it is not 1% of both. [C]

### Reset matrix

| Prestige | What is removed or rebuilt | What survives |
|---|---|---|
| Silver | Awards Silver. Removes current/earned Associates, purchased Engagement generators, and earned Engagement. Current Engagement becomes its CRM-retained fraction, or zero. Clears bar progress. Without Positions, removes Turbocharge/Flywheel/Boost and recruitment autostart. | Silver/Gold/Titanium; current Velocity and its producers; Gold-funded recursive producers; later upgrades; completed quests. |
| Gold | Calculates Gold, calls Silver, then awards Gold. Removes current/earned Silver; purchased Velocity generators; earned Velocity. Current Velocity becomes its CRM fraction, or zero. Removes both quest-completion counters unless Positions >= 3. | Gold/Titanium, Gold-funded recursive generators, their bonus quantities, and upgrade tiers not explicitly removed by the nested Silver action. |
| Titanium | Calculates Titanium, calls Gold, then awards Titanium. Completely removes current/earned Gold, Engagement, and Velocity; current/earned Accelerator/Certification/Onboarding bonuses; and purchased Documentation/MOOC/Partnerships. Without Positions >= 2, removes Community, CRM, Cost Cutting, and Positions itself. It then rechecks Positions before removing early upgrades/autostart. Clears bar progress. | Titanium and its upper-tier upgrades, including Auto Promote and Expansion. With sufficiently developed Positions, the protected upgrade tiers survive. |

**CRM does not preserve Engagement or Velocity through the final Titanium wipe.** Titanium explicitly removes those stats after running the lower reset. Also, Positions level 1 is not enough to preserve Positions itself through Titanium, so its first-tier protection can disappear during that same reset. [C]

### Quests

| Quest | Target | Restriction | Stated reward [D] |
|---|---|---|---|
| Micromanagement | 5956538 Associates | Disables engagement boost | Doubles the rate you get silver members gain. |
| Micromanagement 2 | 9.694551e+11 Associates | Disables engagement boost | Doubles the rate you get silver members gain. |
| Micromanagement 3 | 9.763379e+16 Associates | Disables engagement boost | Doubles the rate you get silver members gain. |
| Micromanagement 4 | 2.767537e+24 Associates | Disables engagement boost | Doubles the rate you get silver members gain. |
| Micromanagement 5 | 3.649871e+33 Associates | Disables engagement boost | Doubles the rate you get silver members gain. |
| Overhead | 1.438799e+14 Associates | Buying any engagement generator increases the cost of all generators | Doubles the rate you get silver members gain. |
| Overhead 2 | 1.278533e+21 Associates | Buying any engagement generator increases the cost of all generators | Doubles the rate you get silver members gain. |
| Overhead 3 | 4.179545e+27 Associates | Buying any engagement generator increases the cost of all generators | Doubles the rate you get silver members gain. |
| Overhead 4 | 5.51205e+36 Associates | Buying any engagement generator increases the cost of all generators | Doubles the rate you get silver members gain. |

Micromanagement disables Engagement's boost, so direct Associate production and other multipliers must carry the run. Overhead makes Engagement purchases mutually inflate cost. Each completed level doubles the Silver reward, and that propagates into Gold and Titanium through the nested conversion formulas. [D/C]

### Walkthrough [I]

1. Recruit manually until Flywheel automates the bar. Buy Engagement and use Turbocharge/Boost to establish the first Silver run.
2. Add Velocity producers after Silver. Treat Velocity as acceleration and Engagement as output growth; both improve Associate generation.
3. Complete reachable quests before converting upward when their added Silver multiplier outweighs the reset delay.
4. Build Gold-funded recursive production, then use Titanium to buy Positions and Auto Promote.
5. Prioritize Positions level 2 before a Titanium reset if rebuilding the Gold-funded upgrade tier would otherwise be expensive; level 3 protects quest progress at Gold.
6. Use passive promotion and retained upgrades to build approximately **1.305249e13 current Titanium Members**, then complete.

### Story and evidence

**SPL 3** presents the licensing dispute. **Deep Divides** depicts Steelman being pushed out of the foundation, his anger at corporate influence, and the first mention of the Great Reset. The player decides to infiltrate Relentless to investigate. These are the game's fictional positions and events. [D]

Completion points to scene **24**, Chapter **12**. Evidence: Silver/Gold reward functions `0x54d7fc` / `0x54d718`; Titanium reward `0x698f14`; reset methods `0x6989a4`, `0x698440`, `0x698064`; Auto Promote `0x54d5ec`; target `0x697d78`.

## Chapter 12 - The Great Reset

### Economy and active play

The active **Investigate** button and five continuous producers generate **Research**. **Record Research** converts accumulated Research into **Discovery**. Discovery is described as adding 1% Research gain per point and pays for structural upgrades and the five timed generators that create bonus Research generators. [D/C]

Rolling Investigation unlocks holding the Investigate button. Deep Research and the long-press upgrade line scale differently, so active Research is not a fixed multiple of passive production. [D/C]

### Generators

| ID | Generator | Output timing | Raw output | Base purchase cost | Cycle input |
|---|---|---|---|---|---|
| 1 | Scripts | per second | 3.320117 Research | 10509.13 Research | - |
| 2 | Network Scanner | per second | 27.66035 Research | 145801.3 Research | - |
| 3 | Log Analyzer | per second | 915.985 Research | 7.447693e+07 Research | - |
| 4 | Reverse Engineering | per second | 78433 Research | 1.866657e+10 Research | - |
| 5 | Port Prober | per second | 5.269336e+07 Research | 1.23158e+20 Research | - |
| 6 | Phishing Simulator | 58.7 s cycle | 0.1353353 Generator 1 | 2.948285e+15 Discovery | - |
| 7 | Vulnerability Scanner | 55.2 s cycle | 0.1353353 Generator 2 | 52052.08 Discovery | - |
| 8 | Credential Scanner | 51.2 s cycle | 0.1353353 Generator 3 | 3.132728e+07 Discovery | - |
| 9 | API Mapping | 48.2 s cycle | 0.1353353 Generator 4 | 1.385961e+13 Discovery | - |
| 10 | Threat Profiler | 46.2 s cycle | 0.1353353 Generator 5 | 2326790 Discovery | - |

The bonus-stat mapping is `Generator 1 = Scripts`, `2 = Network Scanner`, `3 = Log Analyzer`, `4 = Reverse Engineering`, `5 = Port Prober`. Thus Phishing Simulator creates Scripts; Vulnerability Scanner creates Network Scanners; Credential Scanner creates Log Analyzers; API Mapping creates Reverse Engineering; Threat Profiler creates Port Probers. [C]

**Advanced Methods** explicitly links upper-tier purchase costs. Buying any one of these five raises the quantity exponent used by the others. During Refinement, the special price path applies a large disabling cost rather than ordinary upper-tier purchasing. Do not plan a Refinement run around buying a cheap substitute upper-tier generator. [C]

### Upgrade ladder

The appendix supplies the full 25-level Rolling Investigation and 9-level Deep Research price schedules, plus every one-time upgrade price. Some sequences are not monotonically increasing; the original construction order is retained rather than sorted by price. [D]

| Stage | Upgrade effects recovered from the descriptions |
|---|---|
| Active research | Rolling Investigation unlocks holding and improves its yield. Deep Research improves Research. Faster Investigation says 437x long-press Research. **Stronger Network Scanner actually describes 1477x long-press Research**, despite its name. |
| Early generator boosts | Faster Scripts: 6x Scripts; Better Scanner: 15x Network Scanner; Documents: 5000x Scripts; Detailed Logs: 500x Log Analyzer. |
| Bonus units and discounts | Hex Dump: 100 bonus Reverse Engineering; Lookup Table: 10 Port Probers; Macros: 300 Scripts; More Logs: 100 Log Analyzers; More Ports: 20 Port Probers. Black Friday and Lower Prices each say another 99.9% reduction. |
| Foundations | Improved Foundation strengthens Rolling Investigation and Deep Research. Boosted Research: 30x first four generators. Fast Follow: 10x tier-one yields. |
| Upper-tier unlock | Advanced Methods: unlocks the linked-cost generator-generators. |
| Middle acceleration | Foresnics: 10x Log Analyzer and the text's "log scanner"; Code Analysis: twice as fast progress bars; Records: double Discovery on prestige; Backend Logistics and Network Analysis improve named groups. |
| Late acceleration | Efficiency Gains: 90% reduction for most progress generators; Speedy Analysis: 4x upper-tier speed; Reverse Development and Reverse Development 2: stronger lower-tier yields; Downward Pressure: lower upper-tier prices; Overcharge: 8x upper-tier speed; Network Analysis 2: Scripts/Network Scanner boosts. |
| Endgame | Bonuses: two bonus copies of each upper-tier generator, described as surviving prestige. Hyperspeed: 10x upper-tier speed. |

All entries in that table are **[D]**, except the separately described code-confirmed mechanics. "Foresnics" and "log scanner" are the APK's wording; there is no generator literally named Log Scanner in the recovered generator list. Exact large group-yield multipliers hidden behind descriptions such as "significantly" are not supplied here as guessed numbers. [U]

Three costs built at runtime are **10 Discovery** for Improved Foundation, **200 Discovery** for Black Friday, and **5000 Discovery** for Fast Follow. [C]

### Research scaling and prestige

The basic long-press boost helper uses base `1.5 + 0.2*q`, or `1.75 + 0.2*q` with Improved Foundation, raised to its supplied level argument. The ordinary basic boost uses `1.5 + 0.1*q`, or `1.75 + 0.1*q` with Improved Foundation. Here `q` is completed Refinement levels. These are component helpers, not complete output formulas including every upgrade and global bonus. [C]

`Discovery gain ~= earned(Research) * 5.66103201e-13`, doubled when Records is owned. [C]

**Record Research resets:** current and earned Research; current bonus counts for the five lower-tier generators; the entire purchased-generator map; the entire upgrade map; and progress-bar progress. [C]

**Record Research preserves:** current and earned Discovery; completed quest state; and state outside those explicitly cleared fields, including the persistent upper-tier bonus mechanism advertised by Bonuses. It updates a saved high-water field from Discovery before rebuilding. Because the upgrade map is cleared, having a Discovery-denominated price does **not** make an upgrade permanent. Improved Foundation, Black Friday, Fast Follow, Advanced Methods, Records, and other upgrades must be reacquired after an ordinary Record Research reset. [C]

Bonuses' purchase callback adds 2 to each of the five upper-tier bonus stats (`generator_6` through `generator_10`). The reset clears only the five lower-tier bonus stats. Reaching and buying Bonuses again after another Record Research can therefore accumulate another two upper-tier copies; these survive local research prestiges, but not the full chapter-completion reset. [C]

### Quests

| Quest | Target | Restriction | Stated reward [D] |
|---|---|---|---|
| Refinement | 6.980552e+22 Research | Disables generator generators. | Increases the effect of rolling research and deep research. |
| Refinement 2 | 2.554759e+24 Research | Disables generator generators. | Increases the effect of rolling research and deep research. |
| Refinement 3 | 2.459264e+31 Research | Disables generator generators. | Increases the effect of rolling research and deep research. |
| Refinement 4 | 6.667714e+32 Research | Disables generator generators. | Increases the effect of rolling research and deep research. |
| Refinement 5 | 2.520234e+37 Research | Disables generator generators. | Increases the effect of rolling research and deep research. |

Refinement disables generator-generators and rewards the active/basic Research multipliers. Completed levels remain through the normal research prestige. The first two targets are relatively close compared with the subsequent exponential jumps; their permanent effect can help a later active rebuild. [D/C/I]

### Walkthrough [I]

1. Use Investigate to buy Scripts and early active upgrades. Holding becomes the convenient active path after Rolling Investigation.
2. Mix active boosts with the large named generator boosts. A 5000x Scripts upgrade can matter more than saving for a much later generator's base cost.
3. Record Research when the Discovery gain meaningfully improves the next run. Budget to rebuy the structural upgrades because the reset clears them.
4. Unlock Advanced Methods and choose upper-tier purchases carefully: all five share cost growth. A purchase is valuable for the bonus generator it produces, but also makes every alternative more expensive.
5. Complete Refinement levels when direct Research production can meet the target without recursive generation. Use the retained quest reward in the next rebuilt run.
6. Reacquire Records before a large conversion when affordable, then use the late speed and bonus upgrades to reach approximately **1.073308e50 current Research**.

### Story and evidence

**Sitrep:** the player has gained access to Taffy's emails but has not yet identified the Great Reset. **Discovery:** the player learns that it is a license update, SPL 4, involving patents and trademarks, and decides to confront Taffy. [D]

Completion points to scene **26**, Chapter **13**. Evidence: upgrade construction `0x4f69b0`; scaling helpers `0x4fa05c` and `0x544188`; reset `0x69ab30`; reward `0x69ae78`; completion target `0x69a760`.

## Chapter 13 - Free As In Anarchy

### Economy: buy stats, multiply Power

This is the major structural change in the late game. The 27 service purchases have empty ordinary yield maps and populated **on-buy stat maps**. Buying Flexible Container Service immediately grants Compute; buying Route 42 grants Networking, and so forth. You then retain those stat quantities until a research/Sprint reset. The purchases themselves do not continuously produce more of their named stat. [D/C]

The raw Power-per-second calculation is:

`(product(1 + each service stat) - 1) * 2^completed_Bill_Shock_levels`.

The ordinary product includes Compute, Storage, Networking, Databases, Security, Management, Analytics, ML, Dev, and Integration. During Vibe Coding it omits Security. **Management exists in the formula and enum, but no Management-producing purchase appears in the 27 recovered records.** A missing Management stat contributes a factor of 1; no extra service should be invented to fill the category. [C]

Experience supplies a separate Power multiplier `1 + 0.01*Experience`. The on-buy path also uses the shared boost routine with a special `1/6` argument, so the raw on-buy amounts below are not necessarily what a heavily boosted account receives. [C]

### Purchases

| ID | Generator | Output timing | Raw output | Base purchase cost | Cycle input |
|---|---|---|---|---|---|
| 1 | Flexible Container Service | on purchase | 24.04675 Compute | 100 Power | - |
| 2 | Epsilon | on purchase | 671.8264 Compute | 1.957296e+11 Power | - |
| 3 | App Host | on purchase | 28882.76 Compute | 7.58047e+52 Power | - |
| 11 | Shared Storage Service | on purchase | 20.55286 Storage | 1.947203e+13 Power | - |
| 12 | Flexible Block Store | on purchase | 2235.008 Storage | 3.386721e+31 Power | - |
| 13 | Icecap | on purchase | 79379.86 Storage | 1.235939e+96 Power | - |
| 21 | Route 42 | on purchase | 29.42957 Networking | 403.4288 Power | - |
| 22 | Distributed Cloud | on purchase | 2787.304 Networking | 1.257998e+35 Power | - |
| 23 | CloudFort | on purchase | 149986.4 Networking | 2.80399e+70 Power | - |
| 31 | Relational DB | on purchase | 25.81614 Databases | 8.65934e+16 Power | - |
| 32 | Dynamite DB | on purchase | 2257.696 Databases | 1.104267e+39 Power | - |
| 33 | Sky DB | on purchase | 57634.18 Databases | 5.087562e+67 Power | - |
| 41 | IAM | on purchase | 30.6 Security | 3.982478e+07 Power | - |
| 42 | Cognit | on purchase | 7201.179 Security | 6.926625e+25 Power | - |
| 43 | DDoS Shield | on purchase | 417149.5 Security | 1.32664e+57 Power | - |
| 51 | Code Repo | on purchase | 22.60565 Dev | 1.262113e+26 Power | - |
| 52 | Cloud8 | on purchase | 1000.445 Dev | 1.540287e+65 Power | - |
| 53 | Build Pipeline | on purchase | 24543.44 Dev | 4.507658e+79 Power | - |
| 61 | Artemis | on purchase | 27.16692 Analytics | 1.092907e+43 Power | - |
| 62 | SuperSearch | on purchase | 743.9695 Analytics | 1.306042e+94 Power | - |
| 63 | Kinetics | on purchase | 49612.59 Analytics | 1.233562e+101 Power | - |
| 71 | Text to Speech | on purchase | 16.39539 Ml | 5.104204e+58 Power | - |
| 72 | Translate | on purchase | 609.903 Ml | 2.748877e+80 Power | - |
| 73 | Recognition | on purchase | 45387.86 Ml | 7.933117e+97 Power | - |
| 81 | Simple Message Queue | on purchase | 5.646297 Integration | 2.227438e+84 Power | - |
| 82 | Pub/Sub | on purchase | 936.36 Integration | 1.299625e+99 Power | - |
| 83 | EventLink | on purchase | 54951.16 Integration | 1.881877e+112 Power | - |

All base purchase prices are in **Power**. Because some upgrades are paid in service stats, spending those stats can reduce the product that produces Power. This is an actual tradeoff, not an upgrade funded by a disconnected currency. [C/I]

### Research levels and unlocks

The research-level counter starts at zero. **Upgrade Research** checks a Power requirement, resets the service economy, and increments the counter. It does not simply purchase an additive production multiplier. [C]

| Research level earned | Approximate Power requirement for that upgrade | Selected unlocks at that resulting level |
|---|---|---|
| 1 | 5.320482e11 | Epsilon joins Compute |
| 2 | 3.931334e12 | Storage group |
| 3 | 3.185593e16 | Database group |
| 4 | 1.545539e25 | Cognit joins Security |
| 5 | 3.104298e26 | Dev and main-upgrade sections |
| 6 | 8.351551e30 | Flexible Block Store |
| 7 | 6.749860e35 | Distributed Cloud; CloudFort additionally needs Fortress |
| 8 | 1.825340e38 | Dynamite DB |
| 9 | 3.104927e53 | App Host and DDoS Shield |
| 10 | 1.221482e58 | Sky DB, Cloud8, ML group |
| 11 | 5.343858e77 | Build Pipeline and Translate; Recognition additionally needs Bill Shock 12 |
| 12 | 1.309980e88 | Icecap, SuperSearch, Pub/Sub; Kinetics additionally needs Deep Analytics, EventLink needs Vibe Coding 8 |

These are generator-list gates, not promises that the corresponding purchases are immediately affordable. Analytics has an additional unlock path associated with Sprint Planning/story progression; Integration is associated with Microservices. [C]

### Upgrades and automation

| Upgrade | Cost, approximately | Effect |
|---|---|---|
| Sprint Planning | 1450.988 Dev | Unlocks Sprint prestige |
| Core | 242801.6 Compute | Autobuy for the first three stat groups |
| Multitasking | 488942.4 Security | Research upgrades award Experience as if performing a Sprint |
| Strong Start | 33556.97 Networking | Begin a reset with 1 in Compute, Networking, Security, Storage, Databases, and Dev |
| Grow | 247954.4 Databases | Autobuy for the next three groups |
| Back To Basics | 1.167062e6 Compute | 10x Flexible Container Service, Route 42, and IAM yields |
| Fortress | 1.850976e21 Experience | Unlock CloudFort, subject to the research gate |
| More Milestones | 4.399494e6 Databases | Add Bill Shock 6-12 and Vibe Coding 3-8 to the initially shorter quest lists |
| Microservices | 1.238857e6 Dev | Unlock Simple Message Queue / Integration |
| Deep Analytics | 40254.22 Analytics | Unlock Kinetics, subject to the research gate |

Costs and descriptions are [D]; research/quest list gating and reset effects are [C]. No separate named late-tier autobuyer beyond Core and Grow was recovered.

### Sprint and research reset

`Experience gain ~= earned(Power) * 2.40500694e-30`. [C]

| Action | Awards | Resets | Preserves |
|---|---|---|---|
| Sprint Meeting | Adds Experience to current and earned Experience | All purchased generators; current and earned Power and every service stat other than Experience | Experience; research level; upgrade map; quest completions; other saved unlock state |
| Upgrade Research without Multitasking | One research level | Same service-economy reset | Same retained state, but no automatic Experience award |
| Upgrade Research with Multitasking | Sprint Experience, then one research level | Calls the Sprint path, hence the same reset | Same retained state |

After the reset, Power is seeded to **100**, including its earned counter. Strong Start additionally seeds the six listed service stats to **1**, in both current and earned maps. These are reset starting values, not one newly purchased copy of each service. [C]

### Quests

**Bill Shock** requires reaching a Power target within five minutes; it automatically cancels on failure. Every completion doubles raw Power production, and level 12 also unlocks Recognition. [D/C]

| Level | Power target, approximately |
|---|---|
| 1 | 9.200345e11 |
| 2 | 4.119423e21 |
| 3 | 3.036006e23 |
| 4 | 4.619112e27 |
| 5 | 1.017427e32 |
| 6 | 3.211045e40 |
| 7 | 1.384356e56 |
| 8 | 3.667037e66 |
| 9 | 7.677456e73 |
| 10 | 1.249543e79 |
| 11 | 9.989526e85 |
| 12 | 4.268972e97 |

**Vibe Coding** removes Security from the Power product. The table below preserves the stated rewards, which differ from code in several places. [D]

| Quest | Target | Restriction | Stated reward [D] |
|---|---|---|---|
| Vibe Coding 1 | 1.04244e+11 Power | Security does nothing. | 10x Flexible Container Service and Shared Storage Service yields. |
| Vibe Coding 2 | 6.844657e+18 Power | Security does nothing. | 100x cheaper Flexible Block Storage and Code Repo. |
| Vibe Coding 3 | 7.641518e+40 Power | Security does nothing. | 5x relational db |
| Vibe Coding 4 | 9.495701e+63 Power | Security does nothing. | 10x code repo |
| Vibe Coding 5 | 3.859021e+70 Power | Security does nothing. | 2x shared storage service and code repo |
| Vibe Coding 6 | 4.730755e+77 Power | Security does nothing. | 10x yields to shared storage service and relational db |
| Vibe Coding 7 | 2.91757e+83 Power | Security does nothing. | 5x to Dynamite DB and Code Repo |
| Vibe Coding 8 | 4.219556e+95 Power | Security does nothing. | Unlocks EventLink, 5x to Epsilon, Cognit, Artemis, Simple Message Queue, and Text to Speech |

#### Vibe Coding: implemented modifiers versus descriptions

The compiled `checkAllBoosts` routine uses the following completed-level thresholds. These modifiers accumulate as levels increase. [C]

| Completed levels at least | Implemented modifier |
|---|---|
| 1 | 10x Flexible Container Service and **IAM**; the description names Shared Storage Service instead of IAM |
| 2 | 0.01x purchase cost for Flexible Block Store and Code Repo |
| 3 | 5x Relational DB |
| 4 | 2x Shared Storage Service and Code Repo; the level-4 description instead says 10x Code Repo |
| 5 | 10x Shared Storage Service and Relational DB |
| 6 | The code reads the existing boost for Dynamite DB but writes the combined result to **Relational DB**; also adds 5x Code Repo. This can replace Relational DB's previously accumulated boost rather than multiplying it normally. |
| 7 | 5x Epsilon, Cognit, Artemis, Text to Speech, and Simple Message Queue |
| 8 | EventLink's separate display gate is satisfied; no extra level-8 multiplier branch appears in this routine |

The discrepancy is material: later description rewards appear shifted relative to their implementation, and the database write uses a different key from its read. This is reported as compiled behavior, without assuming which version the developer intended. [C]

### Walkthrough [I]

1. Spend the initial 100 Power on Flexible Container Service. Add a new service category when affordable because an additional `(1 + stat)` factor can outperform adding to a large existing category.
2. Make early research upgrades to open new groups. Their resets are worthwhile primarily because of what they unlock.
3. At research level 5, buy Sprint Planning, then the retention/automation helpers. Multitasking is particularly valuable before later research upgrades because it adds Experience to resets you already need to perform.
4. Use Core and Grow to rebuild, complete reachable Bill Shock levels within the timer, and perform Vibe Coding when production without Security can clear its target.
5. Buy More Milestones to expose the rest of both quest series. Follow the implemented reward table when judging the benefit of another Vibe Coding level.
6. Continue research to 12, then combine the late Analytics, ML, and Integration unlocks. Recognition and EventLink specifically depend on the final quest levels.
7. Accumulate approximately **1.380871e115 current Power** for completion. The research tree's final threshold is much lower than this final chapter requirement.

### Story and evidence

**Into The Den:** Taffy calls the Great Reset a license change and dismisses the player's objections. **Infiltration:** the player recognizes SuperSearch as built from the stolen service. **SPL 4:** the player and Leignis examine the fictional license and decide to build a competing POSIX-compatible operating system outside that license. [D]

Completion points to scene **29**, Chapter **14**. Evidence: on-buy path `0x54fe78`; Power product `0x54d9f0`; research thresholds `0x4fa490`; Sprint/reset `0x69eb38` / `0x69ec5c`; quest modifiers `0x689a80`; final target `0x69e22c`.

## Chapter 14 - Return Of The Mach

### Economy and stats

The **Compile** bar produces **Segfaults**. Navigation boosts its output; I/O boosts its speed. Two parallel prestiges create **Ops** from Navigation and **Throughput** from I/O. A higher **Write Shim** prestige creates **Stability** from Segfaults. Stability boosts both Navigation and I/O and buys the final generators of **Functionality**, the chapter's completion currency. [D/C]

The descriptions say each Navigation point adds 1% Segfault production; each I/O point adds 1% main-bar speed; each Ops point adds 1% Navigation gain; each Throughput point adds 1% I/O gain; and each Stability point adds 1% to both Navigation and I/O gain. Quest effects alter parts of this relationship. [D]

### Generators

| ID | Generator | Output timing | Raw output | Base purchase cost | Cycle input |
|---|---|---|---|---|---|
| 0 | Compile | 14.4 s cycle | 0.01329988 Segfaults | exp(999.0) Segfaults | - |
| 1 | ls | per second | 0.009657698 Navigation | 7.337513 Segfaults | - |
| 2 | pwd | per second | 0.0214936 Navigation | 97.25146 Segfaults | - |
| 3 | cd | per second | 0.07136127 Navigation | 5597.078 Segfaults | - |
| 4 | touch | per second | 0.0009303032 Io | 1.325201e+11 Segfaults | - |
| 5 | mv | per second | 0.004525623 Io | 3.01563e+14 Segfaults | - |
| 6 | rm | per second | 0.04329146 Io | 5.349769e+17 Segfaults | - |
| 7 | mkdir | per second | 0.1092649 Io | 1.307737e+20 Segfaults | - |
| 101 | cp | 14.34 s cycle | 0.01622182 Bonus Ls | 5.494078e+45 Segfaults | - |
| 102 | cat | 14.92 s cycle | 0.0220602 Bonus Pwd | 7.116421e+54 Segfaults | - |
| 103 | file | 14.49 s cycle | 0.02674526 Bonus Cd | 1.705526e+72 Segfaults | - |
| 104 | echo | 14.29 s cycle | 0.01087381 Bonus Touch | 2.042166e+41 Segfaults | - |
| 105 | read | 14.35 s cycle | 0.01467811 Bonus Mv | 2.050853e+44 Segfaults | - |
| 106 | head | 14.67 s cycle | 0.01792788 Bonus Rm | 3.51535e+63 Segfaults | - |
| 107 | tail | 14.77 s cycle | 0.0220602 Bonus Mkdir | 1.413224e+72 Segfaults | - |
| 201 | awk | per second | 7.695846e-08 Functionality | 5.141151e+32 Stability | - |
| 202 | sed | per second | 5.366069e-07 Functionality | 1.367639e+46 Stability | - |
| 203 | cron | per second | 2.920371e-06 Functionality | 4.39066e+59 Stability | - |
| 204 | sh | per second | 3.578435e-05 Functionality | 1.888028e+76 Stability | - |

Compile is the main action, flagged as non-purchasable. Its `exp(999)` raw price is not a playable purchase target. Navigation generators are `ls`, `pwd`, `cd`; I/O generators are `touch`, `mv`, `rm`, `mkdir`. Seven timed command generators manufacture bonus copies of those seven earlier generators. `awk`, `sed`, `cron`, and `sh` produce Functionality continuously. [D/C]

### Unlock and upgrade sequence

| Upgrade | Mechanical role |
|---|---|
| Coffee Break | First level advances Compile by one second per tap; second by two seconds |
| Shell Scripts | First automates Compile; second exposes I/O generators |
| Speed Up, first occurrence | Autobuy Navigation; costs about 1416.58 Ops |
| Automation | Preserves Coffee Break and Shell Scripts on the shared early prestige reset |
| Boilerplate / Boilerplate 2 | Seed Navigation and I/O to 1000 / 1000000 after reset |
| Panic | I/O-generator price reduction of 99.9% |
| Auto I/O | Autobuy I/O generators |
| Boosted Navigation / Boosted I/O | 50 bonus copies of each corresponding generator |
| Higher Throughput | 10x Throughput conversion |
| Faster Running | Another 99.9% cost reduction |
| Tribal Knowledge | Strengthens Ops and Throughput conversion coefficients |
| Cost Reducer | Says 99.9% reduction to all upgrade costs; its presence is also tested by the generator-price function |
| Base Boost | Per-purchased-generator improvement to Navigation and I/O production |
| Speed Up, second occurrence | Passive gains of all three prestige stats; costs about 5.055387e61 Stability |
| Auto Automation | Unlocks the seven timed bonus-generator producers |
| Persistence | Protects the lower upgrade set from Write Shim |
| Quest Persistence | Protects quest completion counters from Write Shim |
| Scripting | Unlocks Functionality generators |

The duplicate **Speed Up** name is present in the APK; the two purchases have entirely different prices and effects. They should not be merged into one upgrade. Full costs are in the appendix. [D/C]

### Prestige formulas

- **Rewrite In Rust:** `Ops gain ~= earned(Navigation) * 0.00162053328`; Tribal Knowledge changes the coefficient to `0.00440506616`.
- **Optimize:** `Throughput gain ~= earned(IO) * 0.000066770446`; Tribal Knowledge changes the coefficient to `0.00018150089`. Higher Throughput multiplies this by 10.
- **Write Shim:** `Stability gain ~= earned(Segfaults) * 1.37294372e-23`.

These are the direct conversion functions. [C]

The late Stability-funded Speed Up produces approximately `2.47875218e-7` times the available Ops reward per second, the same fraction of the available Throughput reward, and `1.12535175e-11` times the available Stability reward per second. It does not perform a reset to make those gains. [C]

### Exact reset structure

**Rewrite In Rust and Optimize are parallel.** One awards Ops; the other awards Throughput. Both call `applySharedPrestige`, which:

- Clears Compile progress; zeroes current/earned Segfaults and earned Navigation/I/O.
- Sets current Navigation and I/O to zero, 1000 with Boilerplate, or 1000000 with Boilerplate 2.
- Sets purchased `ls` through `mkdir` to zero.
- Without Automation, removes Compile autostart, Coffee Break, and Shell Scripts.

It does **not** clear Ops, Throughput, Stability, Functionality, the seven bonus-count stats, timed bonus producers, other upgrades, or completed quests. An Ops reset does not simultaneously award Throughput, nor vice versa. [C]

**Write Shim** additionally:

- Awards Stability, then removes current/earned Ops and Throughput.
- Removes all seven current bonus-count stats.
- Removes purchased generator IDs greater than zero and less than 200: both the seven ordinary producers and the seven timed bonus producers. It preserves the main Compile bar and the Functionality generators with IDs 201-204.
- Without Persistence, removes upgrades with IDs below 1000. The high-tier Stability upgrades themselves survive this filter.
- If the retained upgrade map still contains Boosted Navigation/I/O, reseeds the relevant bonus counts to 50.
- Without Quest Persistence, removes quest completion records with IDs below 10.
- Finally calls the same shared early reset, including its Automation check and Boilerplate starting values.

**Functionality and its generators therefore survive Write Shim.** Early automation can disappear on a Shim if Persistence has not retained the Automation upgrade itself. This differs from simply saying that all automation survives every prestige. [C]

### Quests and story-gated progression

| Quest | Target | Restriction | Stated reward [D] |
|---|---|---|---|
| Bottleneck | 1723728 Segfaults | The main bar yield boost is the minimum of the I/O boost and the navigation boost. | Unlocks I/O prestige. |
| Bottleneck 2 | 3.034811e+10 Segfaults | The main bar yield boost is the minimum of the I/O boost and the navigation boost. | Gives a small segfault boost based on the minimum of the I/O boost and the navigation boost. |
| Bottleneck 3 | 1.763091e+18 Segfaults | The main bar yield boost is the minimum of the I/O boost and the navigation boost. | Unlocks the next intermission |
| Bottleneck 4 | 2.289405e+33 Segfaults | The main bar yield boost is the minimum of the I/O boost and the navigation boost. | 4x the power of Navigation. |
| Bottleneck 5 | 3.382108e+63 Segfaults | The main bar yield boost is the minimum of the I/O boost and the navigation boost. | 16x the power of Navigation. |
| rm -rf . | 7.23643e+22 Segfaults | Disables autobuy and buying anything makes yields go to zero before slowly increasing back to normal. | 4x the power of I/O. |
| rm -rf / 2 | 9.938217e+26 Segfaults | Disables autobuy and buying anything makes yields go to zero before slowly increasing back to normal. | 16x the power of I/O. |
| rm -rf / 3 | 4.204781e+31 Segfaults | Disables autobuy and buying anything makes yields go to zero before slowly increasing back to normal. | 64x the power of I/O. |
| rm -rf / 4 | 8.876457e+38 Segfaults | Disables autobuy and buying anything makes yields go to zero before slowly increasing back to normal. | 256x the power of I/O. |

Bottleneck uses the **minimum** of the I/O and Navigation boosts for the main-bar yield, rather than letting a very large value on one side compensate for the other. Its first completion unlocks the I/O prestige. Its third unlocks the next intermission leading into the microkernel/shim progression. [D/C]

The `rm -rf` quest turns autobuy off and makes purchasing suppress yield before it recovers. Its reward descriptions are successive total powers of four: 4, 16, 64, 256. Do not multiply all four displayed totals together as independent rewards. [D]

### Walkthrough [I]

1. Compile, take Coffee Break, and buy Navigation generators. Use Rewrite In Rust to start Ops and purchase Shell Scripts.
2. Automate Compile, then unlock I/O. Buy Automation before repeated early prestiges when it saves substantial rebuilding.
3. Balance I/O and Navigation during Bottleneck. Complete its first level to expose Optimize, then develop Ops and Throughput in alternating runs.
4. Use Boilerplate and autobuy to speed rebuilding. Complete Bottleneck 3 and the story intermission to reach the Shim layer.
5. Gain Stability, then acquire Persistence and Quest Persistence to stop losing expensive lower-layer progress at Shim.
6. Unlock Auto Automation and the timed bonus producers. Their output survives the parallel early prestiges but is cleared by Shim, so treat Shim as a larger investment decision.
7. Unlock Scripting and buy Functionality production. Those generators survive Shim, making additional Stability runs useful while the final goal continues to build.
8. Accumulate approximately **137760.3 Functionality** and complete.

### Story and evidence

**Programmers These Days:** the player chooses Rust to avoid Segfaults. **muKernel:** Leignis proposes a microkernel and a POSIX shim. **Hello World:** the new OS compiles and remains running; they name it **Nexix**, despite Leignis preferring Freax. [D]

Completion points to scene **32**, Chapter **15**. Evidence: conversion methods `0x54e7d4`, `0x54e89c`, `0x54e9cc`; shared reset `0x6a1a6c`; Shim `0x6a15d0`; reset filters `0x6a1d70`-`0x6a1ddc`; passive prestige `0x54e65c`; target `0x6a1118`.

## Chapter 15 - We Don't Break User Space

### Economy: development and promotion

**Symbols** are produced by Generate Symbols and six continuous development generators. **Merge** converts earned Symbols into **Pull Requests**, whose described effect is +1% Symbols per point. Four timed promotion generators consume Symbols to make **Stars**. **Make Release** converts earned Stars into **Hype**, which improves Pull Request gain weakly and is the final completion currency. [D/C]

There are also six bonus-generator counters corresponding to Virtual Table, FFI Bridge, Pointer Export, Interupt Stub, Name Resolver, and Symbol Lock. Generate Symbols can accumulate those bonus counts once the relevant upgrades are present. [C]

### Generators

| ID | Generator | Output timing | Raw output | Base purchase cost | Cycle input |
|---|---|---|---|---|---|
| 1 | Virtual Table | per second | 0.007886522 Symbol | 116.3497 Symbol | - |
| 2 | FFI Bridge | per second | 0.0106308 Symbol | 507.7555 Symbol | - |
| 3 | Pointer Export | per second | 0.01450445 Symbol | 4188.09 Symbol | - |
| 4 | Interupt Stub | per second | 0.02017724 Symbol | 319080.9 Symbol | - |
| 5 | Name Resolver | per second | 0.06095871 Symbol | 1.109339e+11 Symbol | - |
| 6 | Symbol Lock | per second | 0.3945537 Symbol | 2.950688e+16 Symbol | - |
| 11 | Announcement | 14.83 s cycle | 1.40776 Stars | 7.384028e+08 Stars | 51123.52 Symbol |
| 12 | Blog Post | 31.15 s cycle | 4.196287 Stars | 1.231698e+13 Stars | 138968.1 Symbol |
| 13 | Tutorial | 67.24 s cycle | 13.9213 Stars | 1.026776e+15 Stars | 377754.6 Symbol |
| 14 | Benchmark Bragging | 15.15 s cycle | 51.08138 Stars | 5.330624e+18 Stars | 1026843 Symbol |

The spelling **Interupt Stub** is preserved from the APK. Star generators both cost Stars to buy and require Symbols to run. Their cycle input is not a one-time unlock cost. A production plan that buys promotion but neglects Symbols can stall on this input. [D/C/I]

### Upgrades

| Upgrade | Levels | Role |
|---|---|---|
| Bigger Symbols | 8 | Strengthens Generate Symbols |
| Bonus Symbols | 6 | Adds bonus counts to the Symbol generator families |
| Bigger Bonuses | 4 | Strengthens the bonus-count award |
| Multithreading | 7 | Boosts generator-based Symbol production |
| Efficient Symbols | 6 | Adds yield according to purchased Symbol generators |
| Syscall Vector | 6 | Each level contributes another 99% generator-cost reduction |
| Automated Development | 5 | Passively invokes an equivalent amount of Generate Symbols |
| Skilled Developer | 3 | After Merge, restarts the seven Symbol upgrade families at level 1, 2, or 3 |
| Advanced Education | 2 | Strengthens several Symbol-upgrade formulas |
| Public Relations | 3 | Multiplies Merge reward by 10 per level |
| Recruiting Program | 1 | Unlocks the additional buy options for Star generators |

The seven Symbol upgrade families are Bigger Symbols, Bonus Symbols, Bigger Bonuses, Multithreading, Efficient Symbols, Syscall Vector, and Automated Development. Skilled Developer does not restore whatever high level you had; it sets these families to the retention level purchased. [C]

The helper formulas use Advanced Education level `e` as an additive `0.45*e` contribution to upgrade strength. In particular, Bigger Symbols uses `5^(level + 0.45*e)` and Multithreading uses `2^(level + 0.45*e)`. Bigger Bonuses' helper returns a level-based factor scaled by `1 + 0.45*e`. Those helpers feed larger output expressions; they should not be substituted for the entire production formula. [C]

### Merge and Release formulas

Let `p` be Public Relations level and `H = earned(Hype)`:

`Pull Request gain ~= earned(Symbols) / 2928497.35 * 10^p * (1 + max(log10(H), 0))`.

For Hype at or below 1, the logarithmic term is clamped to zero, giving a multiplier of 1. This explains the description that Hype boosts Pull Requests "very slightly": it scales with the decimal logarithm, not linearly with Hype. [C]

`Hype gain ~= earned(Stars) / 400312.191`. [C]

### Exact reset matrix

| Action | Resets / replaces | Preserves |
|---|---|---|
| Merge | Adds current/earned Pull Requests. Sets the six purchased Symbol generator counts to zero. Removes the seven Symbol-upgrade families, or sets each to the Skilled Developer level if owned. Clears current/earned Symbols and all six current/earned Symbol bonus-count stats. Resets an additional local generation accumulator. | Pull Requests, Stars and their earned counter, Hype, purchased promotion generators, Skilled Developer, Advanced Education, Public Relations, Recruiting Program, and completed quests. |
| Make Release | Adds current/earned Hype. Clears bar progress and removes purchased Announcement/Blog Post/Tutorial/Benchmark Bragging. Clears earned Stars. Replaces current Stars with 0.001 times Announcement's raw base purchase cost. | Symbols and their earned counter, Symbol purchases and bonuses, Pull Requests, upgrades, quests, and existing Hype. It does not call Merge. |

During the Automation quest, Merge seeds current Symbols from the first Symbol generator's raw base price (approximately 116.3497), allowing recovery while Generate Symbols is disabled. Outside that quest it removes the balance normally. [C]

The Release restart Stars are important: the new run does not simply retain all previous Stars, nor does it end at an unconditional zero. The routine reads Announcement's raw cost tuple and multiplies by 0.001, giving approximately **738402.8 Stars** in this build. It does not call the quantity-scaled or discounted next-purchase-price function. [C]

#### Current Symbols versus earned Symbols

The active Generate Symbols path calculates the current-balance addition with the Symbol stat boost, but its separate earned-Symbols addition omits that stat-boost lookup. Both include the active base amount, Bigger Symbols, completion-token boost, and supplied action amount. This means the displayed Symbols gained from active generation and the amount credited toward Merge can differ. The prestige formula above deliberately uses the earned counter. This is a code-level discrepancy, not an inferred balancing rule. [C]

### Quests

| Quest | Target | Restriction | Stated reward [D] |
|---|---|---|---|
| Automation | 1.370697e+08 Stars | Disables the generate symbols boost. | 3x speed for star generators and 5x more symbols. |
| Automation 2 | 1.907204e+16 Stars | Disables the generate symbols boost. | 9x speed for star generators and 25x more symbols. |
| Automation 3 | 2.439585e+22 Stars | Disables the generate symbols boost. | 27x speed for star generators and 125x more symbols. |

Automation disables the **Generate Symbols** function: it returns immediately when the quest is active. That affects both manual generation and Automated Development's calls to the same function. Generator-based Symbol production remains the route through the challenge. The reward descriptions are cumulative tier totals: 3/9/27x Star-generator speed and 5/25/125x Symbols. [C/D]

### Walkthrough [I]

1. Generate Symbols, buy continuous development generators, and improve the active and generator upgrade families together.
2. Merge for Pull Requests to strengthen subsequent Symbol production. Before using a large displayed active gain to estimate Merge, remember that the earned counter follows its own calculation.
3. Reach the promotion layer and maintain enough Symbol supply to feed its timed cycles. Promotion creates the Stars used for Skilled Developer and Public Relations.
4. Buy Skilled Developer before frequent Merges when retaining the upgrade floor materially shortens rebuilding. Public Relations directly improves Merge conversion; Advanced Education improves the underlying upgrade strengths.
5. Complete Automation levels once passive Symbol production can carry the run without Generate Symbols or its automated equivalent.
6. Make Releases to accumulate Hype. You retain the development economy, so Release is principally a promotion rebuild rather than a complete return to the beginning.
7. Use repeated development/Merge/Release cycles to reach approximately **7.199113e24 current Hype**. That is the last playable chapter-completion target recovered from this APK.

### Story and the endpoint

**Gitforge:** the player publishes Nexix; Leignis recommends announcements, blog posts, tutorials, benchmarks, and a continuous stream of updates. Releases are presented as a way to package those updates. **Out Of Time:** the boss announces that SPL 4 will be enforced next summer. The player says the OS is ready; Leignis questions what "ready" means. [D]

This is a **cliffhanger**, not a recovered victory over the LSC or a finished operating-system adoption campaign. Completion points to scene **34** and Chapter **16**, whose playable content is absent. [C]

Evidence: Generate Symbols `0x5473f8`; upgrade helpers `0x4fe210`-`0x4fe8c4`; Release `0x6a3ec4`; Release reward `0x6a4148`; Merge `0x6a41d8`; Merge reward `0x6a49d4`; target `0x6a3c48`.

## Chapter 16 - You'll Own Nothing And You'll Be Happy

The title is present in the chapter manager. Its game-state class contains an empty generator map and empty upgrade map. The screen displays **Under development / Come back later**. Its enum/header scaffolding contains Contributions, but that is not evidence of an implemented Contributions economy. [D/C]

No recoverable generators, prices, upgrade effects, prestige rules, automation, quests, completion requirement, completion reward, or playable progression loop were found for this chapter. Chapter 15 can point to it, but that transition does not make the placeholder playable. No subsequent story scene beyond Out Of Time was recovered in the 35-scene story set. [D/C]

## Chapter 17 - Year Of Leinix On The Desktop

This is another named placeholder with empty generator and upgrade maps and the same development message. Contributions appears in the scaffold, but no working production or completion system was recovered. [D/C]

Its title suggests a theme, but no gameplay walkthrough or final ending can be reconstructed from a title alone. **There is no evidence in these APKs for a playable Chapter 18 or a completed Chapter 17 finale.** [D/C]

## Coverage, verification, and remaining limits

### What was recovered

- Seven playable late-chapter state classes and two explicit placeholder classes.
- 106 constant generator records for Chapters 9-15, plus four runtime-built Chapter 9 keyboards.
- All named upgrade families recovered from these classes, with their literal cost schedules and original descriptions in the appendix; four simple runtime-built prices are additionally decoded here.
- 43 constant late-chapter quest records, plus Chapter 13's 12 runtime-built Bill Shock levels.
- The complete remaining 16 story records, including the empty scene.
- Chapter targets, local prestige conversion functions, selective reset writes, several unlock counters and display gates, and the shared completion-token path.

### What the evidence does not establish

This was not an emulator run, an instrumented playthrough, or an inspection of a personal save. It cannot establish actual completion times, the optimal purchase order, or every effect of a live remote configuration. Some secondary multiplier curves and visibility predicates remain only partially interpreted. These are not silently treated as zero or as standard behavior from another chapter. The largest unresolved numeric item in the main progression is Chapter 10's later-level Refactoring reward component.

Where text and implementation conflict, both are shown. In particular, consult the Chapter 10 upgrade discrepancies, Chapter 13 Vibe Coding modifiers, Chapter 14 duplicate Speed Up / reset scope, and Chapter 15 current-versus-earned Symbols behavior before using description text for calculations.

The evidence archive makes this reconstruction reviewable: each chapter's resolved listing retains method addresses and references back to the object pool, and the original APK provenance hashes identify the exact build. Global completion rules and local chapter resets are kept separate throughout this document.
