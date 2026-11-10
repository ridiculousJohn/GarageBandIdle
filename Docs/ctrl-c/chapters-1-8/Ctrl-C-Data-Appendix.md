# Ctrl C: recovered data appendix

Version 1.12.2 (146). Values below are decoded static data, rounded for readability. This appendix supplements the interpreted guide. An absent field is not evidence of a zero value. Runtime-built objects are described in the guide instead. Object addresses identify entries in the supplied evidence archive.

## Chapter 1

### Constant quest records

| Quest | Target | Restriction | Reward | Address |
|---|---|---|---|---|

### Constant generator records

The two cost columns preserve the separate fields rather than guessing their role for every UI type. In Chapter 2, field 28 is the level price and field 2c is the automatic-start purchase. Per-second generators and timed bars must not be compared without accounting for cycle duration.

| Name | Cycle seconds | Raw yield per cycle / unit rate | Cost field 28 | Cost field 2c | Cycle input field 30 | Address |
|---|---:|---|---|---|---|---|

### Upgrade literal cost records

Only literal cost tuples adjacent to named upgrade construction are included here. A blank means that this pass did not resolve a literal schedule; it does not mean free. Some upgrade costs are computed by a closure. The guide explains effects and progression.

| Upgrade / construction label | Literal costs in construction order | Code address |
|---|---|---|
| PARA Organization | Stat.KNOWLEDGE, 328.594651 | 6a6580 |
| Cloud Notes | Stat.KNOWLEDGE, 4889.40026 | 6a6704 |
| Markdown | Stat.KNOWLEDGE, 21912.7717 | 6a6924 |
| Syntax Highlighting | Stat.KNOWLEDGE, 45615.37 | 6a6a90 |
| Delegation | runtime / not resolved | 6a7508 |
| Deadlines | Stat.CAREER_CAPITAL, 456.231338 | 6a7660 |
| Meetings | Stat.CAREER_CAPITAL, 340184951.0 | 6a7efc |
| Snacks | Stat.CAREER_CAPITAL, 186929990000.0 | 6a8068 |

## Chapter 2

### Constant quest records

| Quest | Target | Restriction | Reward | Address |
|---|---|---|---|---|

### Constant generator records

The two cost columns preserve the separate fields rather than guessing their role for every UI type. In Chapter 2, field 28 is the level price and field 2c is the automatic-start purchase. Per-second generators and timed bars must not be compared without accounting for cycle duration.

| Name | Cycle seconds | Raw yield per cycle / unit rate | Cost field 28 | Cost field 2c | Cycle input field 30 | Address |
|---|---:|---|---|---|---|---|
| Design | 250 | LINES: 1.32452e+17 | 7.04e+42 LINES | 9.63667e+52 LINES |  | 82ea11 |
| Marketing | 90 | LINES: 6.01333e+12 | 1.99e+34 LINES | 5.48363e+38 LINES |  | 82ea51 |
| Networking | 30 | LINES: 4.05356e+10 | 7.42e+24 LINES | 2.04e+26 LINES |  | 82ea91 |
| Sales | 25 | LINES: 1.00478e+08 | 7.25e+15 LINES | 1.04e+19 LINES |  | 82ead1 |
| Quality Assurance | 7 | LINES: 19,250 | 55,000 LINES | 1.97e+14 LINES |  | 82eb11 |
| Frontend Developer | 4 | LINES: 19.624 | 24.53 LINES | 15,000 LINES |  | 82eb51 |
| Backend Developer | 1 | LINES: 1.0575 | 4.23 LINES | 500 LINES |  | 82eb91 |

### Upgrade literal cost records

Only literal cost tuples adjacent to named upgrade construction are included here. A blank means that this pass did not resolve a literal schedule; it does not mean free. Some upgrade costs are computed by a closure. The guide explains effects and progression.

| Upgrade / construction label | Literal costs in construction order | Code address |
|---|---|---|
| HR | 6.08101e+08 LINES | 6abba4 |
| Task Switching | 1.01207e+13 LINES; 2.22922e+17 LINES; 4.91019e+21 LINES; 1.08154e+26 LINES; 2.38225e+30 LINES; 6.90029e+11 EXPERIENCE; 1.02145e+15 EXPERIENCE | 6abcd4 |
| Stand Up | 1.3565e+07 EXPERIENCE | 6ac784 |
| Micromanagement | 6.05038e+09 DATA | 6ac8b4 |

## Chapter 3

### Constant quest records

| Quest | Target | Restriction | Reward | Address |
|---|---|---|---|---|
| Bureaucracy | 4.25152e+32 CAREER_CAPITAL | When buying anything yields goes to zero and goes back to normal after a minute. Note: it may seem like purchases don't do anything for quite a long time upon buying them. | Unlocks a new upgrade. | 828891 |
| Management Styles | 9.63471e+25 CAREER_CAPITAL | The main bars for each division do nothing. | 10x Meeting Speed And Yields | 8288b1 |

### Constant generator records

The two cost columns preserve the separate fields rather than guessing their role for every UI type. In Chapter 2, field 28 is the level price and field 2c is the automatic-start purchase. Per-second generators and timed bars must not be compared without accounting for cycle duration.

| Name | Cycle seconds | Raw yield per cycle / unit rate | Cost field 28 | Cost field 2c | Cycle input field 30 | Address |
|---|---:|---|---|---|---|---|
| Rust | not specified | PROGRESS: 6,812.49 | 5.05239e+31 CAREER_CAPITAL |  |  | 82ec11 |
| C++ | not specified | PROGRESS: 5,577.59 | 5.82185e+28 CAREER_CAPITAL |  |  | 82ec51 |
| C# | not specified | PROGRESS: 5,046.81 | 1.20367e+25 CAREER_CAPITAL |  |  | 82ec91 |
| Javascript | not specified | PROGRESS: 4,640.84 | 1.0976e+22 CAREER_CAPITAL |  |  | 82ecd1 |
| Scripting | 12 | PROGRESS: 80.158 |  |  |  | 82ed11 |
| Memory Cache | not specified | PROGRESS: 75.6799 | 7.20049e+10 CAREER_CAPITAL |  |  | 82ed51 |
| CDN | not specified | PROGRESS: 75.6799 | 1.31882e+09 CAREER_CAPITAL |  |  | 82ed91 |
| SSD | not specified | PROGRESS: 83.6392 | 6.13076e+08 CAREER_CAPITAL |  |  | 82edd1 |
| HDD | not specified | PROGRESS: 85 | 5.58735e+07 CAREER_CAPITAL |  |  | 82ee11 |
| Storage | 12 | PROGRESS: 10.8482 |  |  |  | 82ee51 |
| Networking Protocols | 10 | PROGRESS: 64.4008 | 3.75521e+06 CAREER_CAPITAL |  |  | 82ee91 |
| External Networking | 6 | PROGRESS: 38.5735 | 9,897.13 CAREER_CAPITAL |  |  | 82eed1 |
| Internal Networking | 4 | PROGRESS: 25.2797 | 30 CAREER_CAPITAL |  |  | 82ef11 |
| Networking | 12 | PROGRESS: 1 |  |  |  | 82ef51 |
| Attend Meeting | 1 | CAREER_CAPITAL: 1 |  |  | 1 PROGRESS | 82ef91 |

### Upgrade literal cost records

Only literal cost tuples adjacent to named upgrade construction are included here. A blank means that this pass did not resolve a literal schedule; it does not mean free. Some upgrade costs are computed by a closure. The guide explains effects and progression.

| Upgrade / construction label | Literal costs in construction order | Code address |
|---|---|---|
| Schedules | 20 CAREER_CAPITAL | 6af104 |
| VIPs | 616 CAREER_CAPITAL; 1,346.14 CAREER_CAPITAL; 60,174.3 CAREER_CAPITAL; 543,074 CAREER_CAPITAL; 2.41508e+09 CAREER_CAPITAL | 6af224 |
| Faster Meetings | 4,044.73 CAREER_CAPITAL; 9,946.74 CAREER_CAPITAL; 20,030.3 CAREER_CAPITAL; 33,024.3 CAREER_CAPITAL; 109,645 CAREER_CAPITAL; 364,033 CAREER_CAPITAL; 2.43389e+06 CAREER_CAPITAL; 1.47242e+07 CAREER_CAPITAL; 1.2024e+08 CAREER_CAPITAL; 7.99902e+08 CAREER_CAPITAL; 4.85083e+10 CAREER_CAPITAL; 2.39065e+11 CAREER_CAPITAL; 4.35604e+11 CAREER_CAPITAL; 2.15756e+12 CAREER_CAPITAL; 1.7619e+13 CAREER_CAPITAL; 1.59012e+14 CAREER_CAPITAL; 5.83462e+14 CAREER_CAPITAL; 4.76465e+15 CAREER_CAPITAL; 4.75235e+16 CAREER_CAPITAL | 6af360 |
| Larger Meetings | 27,042.6 CAREER_CAPITAL; 66,171.2 CAREER_CAPITAL; 179,872 CAREER_CAPITAL; 543,166 CAREER_CAPITAL; 1.98755e+07 CAREER_CAPITAL; 1.2024e+08 CAREER_CAPITAL; 6.56488e+09 CAREER_CAPITAL; 1.78452e+10 CAREER_CAPITAL; 4.85083e+10 CAREER_CAPITAL; 1.31859e+11 CAREER_CAPITAL; 4.81417e+11 CAREER_CAPITAL; 6.49845e+11 CAREER_CAPITAL; 2.15756e+12 CAREER_CAPITAL; 4.3448e+12 CAREER_CAPITAL; 1.9472e+13 CAREER_CAPITAL; 6.46494e+13 CAREER_CAPITAL; 1.94218e+14 CAREER_CAPITAL | 6af49c |
| Overtime | 203,995 CAREER_CAPITAL; 1.1109e+08 CAREER_CAPITAL; 3.01974e+08 CAREER_CAPITAL; 8.2085e+08 CAREER_CAPITAL; 4.48169e+10 CAREER_CAPITAL; 1.24593e+12 CAREER_CAPITAL; 1.01482e+14 CAREER_CAPITAL; 3.71405e+15 CAREER_CAPITAL; 1.14862e+18 CAREER_CAPITAL | 6af5d8 |
| WFH | 1.33162e+13 CAREER_CAPITAL; 1.20179e+14 CAREER_CAPITAL; 2.03648e+15 CAREER_CAPITAL; 1.06828e+17 CAREER_CAPITAL; 1.58547e+19 CAREER_CAPITAL; 8.65639e+20 CAREER_CAPITAL; 4.72623e+22 CAREER_CAPITAL | 6af714 |
| Better Cables | 3,050.31 CAREER_CAPITAL; 1.3565e+07 CAREER_CAPITAL; 3.57566e+10 CAREER_CAPITAL; 9.93828e+13 CAREER_CAPITAL; 2.96256e+17 CAREER_CAPITAL; 6.52547e+21 CAREER_CAPITAL | 6afda0 |
| Better Switches | 4.27417e+13 CAREER_CAPITAL; 3.01147e+14 CAREER_CAPITAL; 8.97707e+17 CAREER_CAPITAL; 2.24851e+20 CAREER_CAPITAL; 2.70407e+26 CAREER_CAPITAL; 4.14554e+29 CAREER_CAPITAL | 6aff08 |
| Optimization | 7.87382e+23 CAREER_CAPITAL | 6b1488 |
| Hardware Acceleration | 3.52918e+30 CAREER_CAPITAL | 6b15d4 |
| PTO | 3.15282e+30 CAREER_CAPITAL; 9.43751e+33 CAREER_CAPITAL | 6b4d4c |

## Chapter 4

### Constant quest records

| Quest | Target | Restriction | Reward | Address |
|---|---|---|---|---|

### Constant generator records

The two cost columns preserve the separate fields rather than guessing their role for every UI type. In Chapter 2, field 28 is the level price and field 2c is the automatic-start purchase. Per-second generators and timed bars must not be compared without accounting for cycle duration.

| Name | Cycle seconds | Raw yield per cycle / unit rate | Cost field 28 | Cost field 2c | Cycle input field 30 | Address |
|---|---:|---|---|---|---|---|
| Faster SSD | not specified | VELOCITY: 0.0117647 | 6.28773 MONEY |  |  | 82de91 |
| Function | not specified | LINES: 4 | 1,380 LINES |  |  | 82ded1 |
| Variable | not specified | LINES: 2 | 690 LINES |  |  | 82df11 |

### Upgrade literal cost records

Only literal cost tuples adjacent to named upgrade construction are included here. A blank means that this pass did not resolve a literal schedule; it does not mean free. Some upgrade costs are computed by a closure. The guide explains effects and progression.

| Upgrade / construction label | Literal costs in construction order | Code address |
|---|---|---|
| The More You Buy | Stat.MONEY, 149117437000.0; Stat.MONEY, 6.59715511e+16; Stat.MONEY, 1.32507403e+18; Stat.MONEY, 4.33169001e+24 | 4ff3fc |
| Ergonomics | Stat.MONEY, 1101837110000.0; Stat.MONEY, 163527126000000.0; Stat.MONEY, 9.79104631e+18; Stat.MONEY, 7.933767e+22 | 4ff530 |
| Clipboard History | Stat.MONEY, 1000.0 | 4ff684 |
| Notepad | Stat.MONEY, 1000.0 | 4ff6e8 |
| Persuasion | Stat.MONEY, 3000.0 | 4ff74c |
| Documentation | 6,000 MONEY | 4ff7e0 |
| Organization | 8,000 MONEY | 4ff874 |
| Series A | 13,000 MONEY | 4ff914 |
| Cost Cutting | 28,000 MONEY | 4ff9ac |
| Autocomplete | 78,200 MONEY | 4ffa48 |
| Better Hardware | 173,000 MONEY | 4ffae4 |
| Dictionary | 336,000 MONEY | 4ffb80 |
| Longer Clipboard History | 584,000 MONEY | 4ffc1c |
| Memory | 633,000 MONEY | 4ffcb8 |
| Marketing | 1.92e+06 MONEY | 4ffd54 |
| More Investors | 7.43e+06 MONEY | 4ffdf0 |
| Continuous Integration | 2.83e+07 MONEY | 4ffea4 |
| \'The Key\'\' | 4.54e+07 MONEY | 4fff44 |
| Review | 9.33e+07 MONEY | 4fffe0 |
| Delegation | 1.65e+08 MONEY | 50007c |
| Series B | 2.92e+08 MONEY | 500124 |
| Text Expansion | 8.52e+08 MONEY | 5001c4 |
| IDE | 3.43e+09 MONEY | 500278 |
| Moore\'s Law | 9.33e+09 MONEY | 500330 |
| Work From Home | 2.58e+10 MONEY | 5003d0 |
| Retrospectives | 8.09e+10 MONEY | 50046c |
| Bare Metal | 1.74e+11 MONEY | 500508 |
| Even More Investors | 3.83e+11 MONEY | 5005a4 |
| Institutionalized Knowledge | 9.21e+11 MONEY | 500640 |
| Sticky Keys | 2.74e+12 MONEY | 5006e0 |
| Improvements | 4.74e+12 MONEY | 500780 |
| Home Office | 5.58e+12 MONEY | 500820 |
| Templates | 6.24e+12 MONEY | 5008c0 |
| Meta Programming | 1.53e+13 MONEY | 500960 |
| Series C | 7.84e+13 MONEY | 500a0c |
| Books | 3.96e+14 MONEY | 500ac8 |
| Keyboard Macros | 5.52e+15 MONEY | 500b6c |
| More Retrospectives | 1.2e+17 MONEY | 500c0c |
| Lighting | 7.58e+17 MONEY | 500cac |
| Multi-Monitor | 1.55e+18 MONEY | 500d64 |
| Syntactic Sugar | 5.91e+18 MONEY | 500e08 |
| IPO | 1.85e+19 MONEY | 500eb4 |
| Analysis | 2.09e+19 MONEY | 500f58 |
| Fast Typing | 2.42e+19 MONEY | 500ff8 |
| Money | 4.85e+19 MONEY | 501098 |
| Insiders | 2.83e+20 MONEY | 501138 |
| Activist Investors | 5.25e+21 MONEY | 5011d4 |

## Chapter 5

### Constant quest records

| Quest | Target | Restriction | Reward | Address |
|---|---|---|---|---|
| Direct To Consumer | 3.42542e+15 POWER | Generator generators generate search power instead of more generators | Generator generators go 10× as fast | 8288d1 |
| Game Jam 2 | 1.97764e+07 POWER | No tech debt but level will reset in 1 minute | Unlocks generator generator generators. | 8288f1 |
| Game Jam | 1,540.71 POWER | No tech debt but level will reset in 1 minute | Unlocks generator generators. | 828911 |
| Legacy Systems | 18,215 POWER | Start with 0.1 Tech Debt | Decreases the speed you accumulate tech debt by 50% | 828931 |

### Constant generator records

The two cost columns preserve the separate fields rather than guessing their role for every UI type. In Chapter 2, field 28 is the level price and field 2c is the automatic-start purchase. Per-second generators and timed bars must not be compared without accounting for cycle duration.

| Name | Cycle seconds | Raw yield per cycle / unit rate | Cost field 28 | Cost field 2c | Cycle input field 30 | Address |
|---|---:|---|---|---|---|---|
| Analysis | 1320 | GENERATOR_8: 1 | 1.05925e+11 POWER |  |  | 82dfd1 |
| Inverted Index | 1285 | GENERATOR_7: 1 | 1.58431e+10 POWER |  |  | 82e011 |
| Document Indexing | 1260 | GENERATOR_6: 1 | 2.55163e+09 POWER |  |  | 82e051 |
| Tokenization | 40.2 | GENERATOR_3: 1 | 4.12852e+08 POWER |  |  | 82e111 |
| Ranking | 38.9 | GENERATOR_2: 1 | 1.02336e+06 POWER |  |  | 82e151 |
| Scoring | 37.4 | GENERATOR_1: 1 | 19,148.9 POWER |  |  | 82e191 |

### Upgrade literal cost records

Only literal cost tuples adjacent to named upgrade construction are included here. A blank means that this pass did not resolve a literal schedule; it does not mean free. Some upgrade costs are computed by a closure. The guide explains effects and progression.

| Upgrade / construction label | Literal costs in construction order | Code address |
|---|---|---|
| Micromanagement | Chapter5Stat.KNOWLEDGE, 1.0; Chapter5Stat.KNOWLEDGE, 29799435.3 | 501af4 |
| Cloud Computing | Chapter5Stat.KNOWLEDGE, 3714.50238; Chapter5Stat.KNOWLEDGE, 202804.958; Chapter5Stat.KNOWLEDGE, 30098924.6; Chapter5Stat.KNOWLEDGE, 12142772800.0 | 501c10 |
| Economies Of Scale | Chapter5Stat.SERENDIPITY, 228661.952 | 501d1c |
| Planning | Chapter5Stat.SERENDIPITY, 2501108820.0; Chapter5Stat.SERENDIPITY, 1009019320000.0; Chapter5Stat.FEATURE, 26.5757727; Chapter5Stat.FEATURE, 79221.2619 | 501e2c |
| Yields | runtime / not resolved | 501f7c |
| Cost | runtime / not resolved | 5020cc |
| Tech Debt | runtime / not resolved | 502204 |
| Yields | runtime / not resolved | 502334 |
| Cost | runtime / not resolved | 50246c |
| Tech Debt | runtime / not resolved | 5025a4 |
| Yields | runtime / not resolved | 5026d4 |
| Cost | runtime / not resolved | 50280c |
| Tech Debt | runtime / not resolved | 502944 |
| Yields | runtime / not resolved | 502a74 |
| Speed | runtime / not resolved | 502bac |
| Cost | runtime / not resolved | 502ce4 |
| Yields | runtime / not resolved | 502e1c |
| Speed | runtime / not resolved | 502f54 |
| Cost | runtime / not resolved | 50308c |
| Yields | runtime / not resolved | 5031c4 |
| Speed | runtime / not resolved | 5032fc |
| Cost | runtime / not resolved | 503434 |
| Yields | runtime / not resolved | 50356c |
| Speed | runtime / not resolved | 5036a4 |
| Cost | runtime / not resolved | 5037dc |
| Yields | runtime / not resolved | 503914 |
| Speed | runtime / not resolved | 503a4c |
| Cost | runtime / not resolved | 503b84 |
| Yields | runtime / not resolved | 503cbc |
| Speed | runtime / not resolved | 503df4 |
| Cost | runtime / not resolved | 503f2c |

## Chapter 6

### Constant quest records

| Quest | Target | Restriction | Reward | Address |
|---|---|---|---|---|
| Triage 2 | 1.33416e+20 ISSUE | Disables commits | Another 10x generator generator speed. | 828951 |
| Triage | 4.47559e+16 ISSUE | Disables commits | 10x generator generator speed. | 828971 |
| Internal Review 3 | 2.40762e+16 COMMIT | Increases all costs by 100x, complete in 3 minutes | Increases each issue generator's yields by the number of non-bonus generators owned times 0.03. | 828991 |
| Internal Review 2 | 6.13585e+10 COMMIT | Increases all costs by 100x, complete in 3 minutes | Increases each issue generator's yields by the number of non-bonus generators owned times 0.02. | 8289b1 |
| Internal Review | 9.22488e+07 COMMIT | Increases all costs by 100x, complete in 3 minutes | Increases each issue generator's yields by the number of non-bonus generators owned times 0.01. | 8289d1 |
| Bug Bash | 3.90973e+18 VELOCITY | No special conditions. | Unlocks Auto Buyers for issue and velocity generators | 8289f1 |

### Constant generator records

The two cost columns preserve the separate fields rather than guessing their role for every UI type. In Chapter 2, field 28 is the level price and field 2c is the automatic-start purchase. Per-second generators and timed bars must not be compared without accounting for cycle duration.

| Name | Cycle seconds | Raw yield per cycle / unit rate | Cost field 28 | Cost field 2c | Cycle input field 30 | Address |
|---|---:|---|---|---|---|---|
| Wireframe Refinement | 277.2 | BONUS_UX_DESIGN: 1 | 4.20857e+15 COMMIT |  |  | 82e211 |
| API Design | 298.2 | BONUS_DOCUMENTATION: 1 | 6.68445e+14 COMMIT |  |  | 82e251 |
| Test Case Creation | 307.8 | BONUS_BUG_REPORT: 1 | 3.1387e+14 COMMIT |  |  | 82e291 |
| Requirements Definitions | 313.8 | BONUS_FEATURE_IDEA: 1 | 1.67165e+14 COMMIT |  |  | 82e2d1 |
| UX Design | not specified | ISSUE: 3.42123 | 2.47067e+06 COMMIT |  |  | 82e311 |
| Documentation | not specified | ISSUE: 3.12677 | 186,461 COMMIT |  |  | 82e351 |
| Bug Report | not specified | ISSUE: 2.80107 | 31,539.6 COMMIT |  |  | 82e391 |
| Feature Idea | not specified | ISSUE: 2.2705 | 1,993.39 COMMIT |  |  | 82e3d1 |
| Git Pull | not specified | VELOCITY: 2.38214 | 5.86686e+08 COMMIT |  |  | 82e411 |
| Git Push | not specified | VELOCITY: 2.18147 | 5.55384e+06 COMMIT |  |  | 82e451 |
| Git Commit | not specified | VELOCITY: 1.85893 | 2.56893e+06 COMMIT |  |  | 82e491 |
| Git Add | not specified | VELOCITY: 1.58407 | 1,993.39 COMMIT |  |  | 82e4d1 |
| Git Status | not specified | VELOCITY: 1.37713 | 1,008.25 COMMIT |  |  | 82e511 |
| Main | 15.6 | COMMIT: 1 | 230.973 ISSUE |  | 1 ISSUE | 82e551 |

### Upgrade literal cost records

Only literal cost tuples adjacent to named upgrade construction are included here. A blank means that this pass did not resolve a literal schedule; it does not mean free. Some upgrade costs are computed by a closure. The guide explains effects and progression.

| Upgrade / construction label | Literal costs in construction order | Code address |
|---|---|---|
| Faster Issue Creation | Chapter6Stat.COMMIT, 81633.9085; Chapter6Stat.COMMIT, 994504.597; Chapter6Stat.COMMIT, 3251052810000.0 | 5061b4 |
| Collaboration | Chapter6Stat.COMMIT, 29799435.3; Chapter6Stat.COMMIT, 8906079760.0 | 5062dc |
| QA Tools | Chapter6Stat.COMMIT, 88830864500.0; Chapter6Stat.COMMIT, 10793875400000.0 | 5063f8 |
| Wiki | Chapter6Stat.COMMIT, 2641174240000000.0 | 506530 |

## Chapter 7

### Constant quest records

| Quest | Target | Restriction | Reward | Address |
|---|---|---|---|---|
| No Comments Ever 2 | 3.25105e+12 POWER | All generators produce additional documentation tech debt if they do not already do so. | Reduces the effect of efficiency on generating tech debt by 90%. | 828a11 |
| No Comments Ever | 7.34845e+06 POWER | All generators produce additional documentation tech debt if they do not already do so. | Reduces the effect of efficiency on generating tech debt by 90%. | 828a31 |
| Last Minute Optimizations 5 | 1.4388e+14 POWER | Halves efficiency boosts. | Upon prestige start with one resource, innovation, development, test lab, and server farm generator. Doubles the effect of efficiency and reduces tech debt again. | 828a51 |
| Last Minute Optimizations 4 | 1.30525e+13 POWER | Halves efficiency boosts. | Upon prestige start with one resource, innovation, development, and test lab generator. Doubles the effect of efficiency and reduces tech debt again. | 828a71 |
| Last Minute Optimizations 3 | 3.56643e+11 POWER | Halves efficiency boosts. | Upon prestige start with one resource, innovation, and development generator. Doubles the effect of efficiency and reduces tech debt again. | 828a91 |
| Last Minute Optimizations 2 | 3.96194e+09 POWER | Halves efficiency boosts. | Upon prestige start with one resource and innovation generator. Doubles the effect of efficiency and reduces tech debt again. | 828ab1 |
| Last Minute Optimizations | 2.19129e+06 POWER | Halves efficiency boosts. | Start with one resource generator. Doubles the effect of efficiency and reduces tech debt. | 828ad1 |

### Constant generator records

The two cost columns preserve the separate fields rather than guessing their role for every UI type. In Chapter 2, field 28 is the level price and field 2c is the automatic-start purchase. Per-second generators and timed bars must not be compared without accounting for cycle duration.

| Name | Cycle seconds | Raw yield per cycle / unit rate | Cost field 28 | Cost field 2c | Cycle input field 30 | Address |
|---|---:|---|---|---|---|---|

### Upgrade literal cost records

Only literal cost tuples adjacent to named upgrade construction are included here. A blank means that this pass did not resolve a literal schedule; it does not mean free. Some upgrade costs are computed by a closure. The guide explains effects and progression.

| Upgrade / construction label | Literal costs in construction order | Code address |
|---|---|---|
| Pair Programming | Chapter7Stat.KNOWLEDGE, 3.70617371; Chapter7Stat.KNOWLEDGE, 49.898952; Chapter7Stat.KNOWLEDGE, 497.701251; Chapter7Stat.KNOWLEDGE, 8955.2927; Chapter7Stat.KNOWLEDGE, 29732.6189; Chapter7Stat.KNOWLEDGE, 1088161.36; Chapter7Stat.KNOWLEDGE, 59411596.9; Chapter7Stat.KNOWLEDGE, 8817462790.0; Chapter7Stat.KNOWLEDGE, 29275007400.0; Chapter7Stat.KNOWLEDGE, 291994265000.0 | 5073c8 |
| Templates | Chapter7Stat.KNOWLEDGE, 15.7998429; Chapter7Stat.KNOWLEDGE, 66.686331; Chapter7Stat.KNOWLEDGE, 1998.1959; Chapter7Stat.KNOWLEDGE, 6634.24401; Chapter7Stat.KNOWLEDGE, 362217.45; Chapter7Stat.KNOWLEDGE, 10853519.9; Chapter7Stat.KNOWLEDGE, 146128949.0; Chapter7Stat.KNOWLEDGE, 2935078390.0; Chapter7Stat.KNOWLEDGE, 21687458900.0; Chapter7Stat.KNOWLEDGE, 160249851000.0 | 507548 |
| APIs | Chapter7Stat.KNOWLEDGE, 27.3851255; Chapter7Stat.KNOWLEDGE, 249.635037; Chapter7Stat.KNOWLEDGE, 5431.65959; Chapter7Stat.KNOWLEDGE, 442413.392; Chapter7Stat.KNOWLEDGE, 80197267.4; Chapter7Stat.KNOWLEDGE, 929353741.0; Chapter7Stat.KNOWLEDGE, 6867046930.0; Chapter7Stat.KNOWLEDGE, 18666568900.0; Chapter7Stat.KNOWLEDGE, 137928325000.0 | 5076c8 |
| Compartmentalization | Chapter7Stat.KNOWLEDGE, 128027.453 | 50783c |
| Not Like That | Chapter7Stat.KNOWLEDGE, 4457060.38; Chapter7Stat.KNOWLEDGE, 110441884.0; Chapter7Stat.KNOWLEDGE, 7364964010.0; Chapter7Stat.KNOWLEDGE, 147929257000.0; Chapter7Stat.KNOWLEDGE, 2971238540000.0 | 50794c |
| Protocols | Chapter7Stat.KNOWLEDGE, 13389749.4; Chapter7Stat.KNOWLEDGE, 300212165.0; Chapter7Stat.KNOWLEDGE, 2403038940.0; Chapter7Stat.KNOWLEDGE, 9941661530.0; Chapter7Stat.KNOWLEDGE, 73459494800.0 | 507a8c |

## Chapter 8

### Constant quest records

| Quest | Target | Restriction | Reward | Address |
|---|---|---|---|---|

### Constant generator records

The two cost columns preserve the separate fields rather than guessing their role for every UI type. In Chapter 2, field 28 is the level price and field 2c is the automatic-start purchase. Per-second generators and timed bars must not be compared without accounting for cycle duration.

| Name | Cycle seconds | Raw yield per cycle / unit rate | Cost field 28 | Cost field 2c | Cycle input field 30 | Address |
|---|---:|---|---|---|---|---|
| Bicho Generator | 1 | BICHO_SHARES: 1 | exp(9999.0) MONEY |  |  | 82e591 |
| Components | 1.21 | BONUS_PC: 1 | 1.28207e+08 MONEY |  |  | 82e5d1 |
| Meetings | not specified | PRODUCTIVITY: 0.0308074 | 4,105.16 MONEY |  |  | 82e611 |

### Upgrade literal cost records

Only literal cost tuples adjacent to named upgrade construction are included here. A blank means that this pass did not resolve a literal schedule; it does not mean free. Some upgrade costs are computed by a closure. The guide explains effects and progression.

| Upgrade / construction label | Literal costs in construction order | Code address |
|---|---|---|
| Discipline | runtime / not resolved | 508744 |
| Metrics | Chapter8Stat.SHARES, 250.385067 | 5088a8 |
| Bulk Buy | Chapter8Stat.PRODUCTIVITY, 2571500.14 | 5089d4 |
| Market Manipulation | Chapter8Stat.SHARES, 92.7585611; Chapter8Stat.SHARES, 83283.0228; Chapter8Stat.SHARES, 1239229.04; Chapter8Stat.SHARES, 96013561.0 | 508ae4 |
| Economies Of Scale | Chapter8Stat.MONEY, 153966364000.0; Chapter8Stat.MONEY, 50855022300000.0; Chapter8Stat.MONEY, 1.62003412e+30; Chapter8Stat.MONEY, 7.8598417e+38 | 508c60 |
| Metrication | Chapter8Stat.MONEY, 9.19480373e+16 | 508d94 |
| Market Power | Chapter8Stat.SHARES, 2571500.14; Chapter8Stat.SHARES, 9435596.91; Chapter8Stat.SHARES, 38263232.3; Chapter8Stat.SHARES, 768537564.0 | 508ea4 |
| Poison Pill | Chapter8Stat.SHARES, 28566.7862; Chapter8Stat.SHARES, 384615.726; Chapter8Stat.SHARES, 4239686.98; Chapter8Stat.SHARES, 63085404.9; Chapter8Stat.SHARES, 197253448.0 | 508ff4 |
| Snowballing Productivity | Chapter8Stat.PRODUCTIVITY, 4206934200.0; Chapter8Stat.PRODUCTIVITY, 119909125000.0; Chapter8Stat.PRODUCTIVITY, 588004243000.0; Chapter8Stat.PRODUCTIVITY, 87267567200000.0 | 509134 |
| Compound Interest | Chapter8Stat.MONEY, 1.81370674e+25; Chapter8Stat.MONEY, 4.80434994e+35; Chapter8Stat.MONEY, 8.20178291e+37 | 509268 |
| Benefits Program | Chapter8Stat.SHARES, 569346631.0 | 509390 |
