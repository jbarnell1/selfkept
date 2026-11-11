# Cross-reference: lab review, test-to-test variation and the link catalogue

How the app reads one blood panel against the one before it, which readings bear on each other,
and what the rules say about it. The code is the source of truth:
`backend/src/SelfKept.Infrastructure/Services/Labs/LabVariation.cs` (variation),
`LabReview.cs` (verdicts), `LabLinks.cs` (links) and
`Services/Interpret/Rules/CrossReferenceRules.cs` (rules). The wire shapes are in
`docs/labs-design.md`, "API".

A figure appears here, in the code or in the app's wording only when its source was opened and
the figure was seen there (the research notes call this VERIFIED). Papers are cited, never a
database of figures. Where a source gives only a direction, the app says only the direction.
Nothing here is a diagnosis, and nothing advises on a medicine.

## Test-to-test variation

A marker moves between two tests in a healthy person with nothing changed. The within-person
biological variation (CVi) says by how much. The reference change value (RCV) is how far a
marker must move between two tests before the change is probably real:

```
RCV = 2.77 x sqrt(CVa^2 + CVi^2)        (2.77 = 1.96 x sqrt(2): two tests, 95 percent)
```

CVa is the laboratory's own analytical variation. No analytical figure has been checked for the
labs the app sees, so CVa is taken as 0. The RCV the app uses is therefore a lower bound: a lab's
imprecision would widen it, and a change just past it is less certain than the word "working"
suggests. When a verified analytical figure is added for a lab, `LabVariation.Rcv` takes it as
its second argument.

| Marker | CVi % | RCV % (CVa 0) | Source | Note |
| --- | --- | --- | --- | --- |
| Total testosterone | 10.0 | 27.7 | Itkonen 2024, Clin Chim Acta 555:117806 | Men only; no figure for women, so a woman's testosterone is "cannot tell" |
| Vitamin D (25-OH) | 6.3 | 17.5 | Cavalier 2021, Nutrients 13(2):431 | After outlier removal; the authors judge the usual model a poor fit for vitamin D |
| Ferritin | 16.9 | 46.8 | Ozkanay 2023, Scand J Clin Lab Invest (PMID 37961767) | 22 volunteers, single centre |
| TSH | 17.7 | 49.0 | Bottani 2022, Clin Chem Lab Med 60(4):523-532 | EuBIVAS |
| Free T3 | 5.0 | 13.9 | Bottani 2022 | EuBIVAS |
| Free T4 | 4.8 | 13.3 | Bottani 2022 | EuBIVAS |
| ALT | 9.3 | 25.8 | Carobene 2017, Clin Chem 63(6):1141-1150 | EuBIVAS. A meta-analysis gives 15.4 (Aarsand 2018, Clin Chem 64(3):501); the EuBIVAS figure is used |
| AST | 9.5 | 26.3 | Carobene 2017 | EuBIVAS |
| GGT | 8.9 | 24.7 | Carobene 2017 | EuBIVAS |
| ALP | 5.3 | 14.7 | Carobene 2017 | EuBIVAS |
| Creatinine | 4.4 | 12.2 | Carobene 2017, Clin Chem 63(9):1527-1536 | EuBIVAS, enzymatic method |
| HbA1c | 1.2 | 3.3 | Ricos 2020, Adv Lab Med 1(3):20200029 | |
| Fasting glucose | 5.0 | 13.9 | Ricos 2020 | |

Not held, so the marker is judged "cannot tell" whatever it does: ApoB, LDL, HDL, total
cholesterol, triglycerides and the ratios (no published lipid or ApoB figure was seen in the
abstracts opened), hs-CRP (the review found pools total and within-person CVs together), and every
other marker without a row above.

## Verdicts

Each marker on a panel is judged against the same marker on the previous panel
(`LabReview.Judge`):

| Verdict | When |
| --- | --- |
| `working` | Moved the wanted way by more than its RCV |
| `right_direction` | Moved the wanted way by less than its RCV |
| `no_clear_change` | Moved the other way by less than its RCV, did not move, or has no wanted way (a range marker inside its range on both panels) |
| `not_working` | Moved the other way by more than its RCV |
| `cannot_tell` | No previous value, no verified variation figure, or a draw condition or substance known to move it differs between the two draws (or, for an interfering substance, is present at either) |

The first four names are the proposal's; `not_working` is added so a real change the wrong way is
never called "no clear change". The wanted way is the marker's good direction (down for LDL, up for
HDL); for a range marker it is back toward the range when the earlier value sat outside it, and
none when it sat inside. Every verdict carries `reasons`:
`no_previous`, `no_variation_figure`, `draw_conditions_differ`, `substance_at_draw`,
`no_wanted_direction`.

An unanswered condition ("unknown") is never counted as a difference. Changes that ran together
(two trials on the same marker) share the credit: the review marks them `sharedCredit`.

## Draw conditions

Asked when a panel is confirmed: fasting (yes, no, unknown), hours since the last meal, the time
of the draw (or just morning, afternoon or evening), and two optional yes or no answers, hard
training in the days before and being unwell around the draw. A Rythm panel is pre-filled as a
morning draw after a fast of 8 or more hours, because Rythm asks for that, and is marked
`defaulted` until the person changes it. Morning is before 12:00 local time, afternoon 12:00 to
17:59, evening from 18:00: the app's own split. Substances come from the medicines list: the tags
of the items active on the day of the draw.

Hard training and illness are stored and named when they differ between two panels, but no
verified link ties them to a marker yet, so they do not make a verdict "cannot tell" on their own.

## Link catalogue

One entry per tie between two readings. `from` is a metric key, `condition:not_fasting`,
`condition:not_morning` or `substance:<tag>`; `to` is always a metric key. Direction: `same`,
`opposite`, `modifies` (changes how `to` should be read) or `none` (named because it does not
move `to`).

| Id | From | To | Direction | What the source shows | Source | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| apob_ldl | lab.apob | lab.ldl | same | When they disagree ApoB tracks heart risk more closely: in 389,529 UK adults only ApoB was linked with heart attack with non-HDL cholesterol and triglycerides in the model | Marston 2022, JAMA Cardiol; Johannesen 2021, JACC | strong |
| ldl_triglycerides | lab.triglycerides | lab.ldl | modifies | Labs usually do not report a calculated LDL at triglycerides of 400 mg/dL or more because of large errors | Sampson 2020, JAMA Cardiol; Martin 2013, JAMA | strong |
| ferritin_hscrp | lab.hscrp | lab.ferritin | modifies | With infection or inflammation a ferritin below 70 ug/L in adults may indicate iron deficiency | WHO 2020 (eLENA) | strong; guideline |
| ferritin_triglycerides | lab.triglycerides | lab.ferritin | same | Ferritin above 300 (men) or 200 (women) with metabolic features usually reflects metabolic health, not iron overload | Valenti 2023, Nat Rev Endocrinol | moderate to strong |
| ferritin_hdl | lab.hdl | lab.ferritin | opposite | As above | Valenti 2023 | moderate to strong |
| ferritin_weight | weight_kg | lab.ferritin | same | As above | Valenti 2023 | moderate to strong |
| ferritin_transferrin_saturation | lab.transferrin_saturation | lab.ferritin | modifies | A saturation persistently above 50 % rules out the metabolic explanation | Valenti 2023 | moderate to strong |
| tsh_ldl | lab.tsh | lab.ldl | same | Hypothyroidism is a secondary cause to rule out; levothyroxine in subclinical hypothyroidism lowered LDL by 0.62 mmol/L | NICE NG238 1.4.3; Abreu 2017, Endocr Connect | strong to moderate |
| tsh_total_cholesterol | lab.tsh | lab.total_cholesterol | same | As above, total cholesterol 0.62 mmol/L | NICE NG238; Abreu 2017 | strong to moderate |
| creatine_creatinine | substance:creatine | lab.creatinine | same | Creatine can raise blood creatinine; transient rises are unlikely to reflect lower kidney function | Antonio 2021, J Int Soc Sports Nutr | moderate |
| creatine_egfr | substance:creatine | lab.egfr | opposite | eGFR is worked out from creatinine, so it can fall on paper | Antonio 2021 | moderate |
| biotin_tsh, biotin_free_t4, biotin_free_t3 | substance:biotin | lab.tsh, lab.free_t4, lab.free_t3 | modifies | Biotin interferes with biotinylated assays; 10 mg a day gave falsely high and falsely low results | Li 2017, JAMA; FDA 2022 | strong; depends on the lab method |
| not_fasting_triglycerides | condition:not_fasting | lab.triglycerides | same | 1 to 6 h after a usual meal, up to +0.3 mmol/L (26 mg/dL) | Nordestgaard 2016, Eur Heart J | strong |
| not_fasting_ldl | condition:not_fasting | lab.ldl | opposite | -0.2 mmol/L (8 mg/dL) | Nordestgaard 2016 | strong |
| not_fasting_total_cholesterol | condition:not_fasting | lab.total_cholesterol | opposite | -0.2 mmol/L (8 mg/dL) | Nordestgaard 2016 | strong |
| not_fasting_remnant_cholesterol | condition:not_fasting | lab.remnant_cholesterol | same | +0.2 mmol/L (8 mg/dL) | Nordestgaard 2016 | strong |
| not_fasting_hdl | condition:not_fasting | lab.hdl | none | Not affected | Nordestgaard 2016 | strong |
| not_fasting_apob | condition:not_fasting | lab.apob | none | Not affected | Nordestgaard 2016 | strong |
| not_morning_testosterone | condition:not_morning | lab.total_testosterone | opposite | At 30 to 40 years, 20 to 25 % lower at 16:00 than at 08:00; about 10 % at 70 | Brambilla 2009, JCEM | strong; 66 men |
| not_fasting_testosterone | condition:not_fasting | lab.total_testosterone | modifies | Guideline: fasting morning total testosterone as the initial test, confirmed by a repeat | Bhasin 2018, JCEM (Endocrine Society) | strong; guideline |

"Read with care" on a review marker is every link from a condition or a substance that holds on
the latest draw. The prep notes and the retest line come from the same links: a fasting morning
retest for testosterone drawn after the morning or not fasting (Bhasin 2018; Brambilla 2009), and
a fasting repeat when non-fasting triglycerides are above 440 mg/dL, 5 mmol/L (Nordestgaard 2016).
No other retest timing is given: none of the "time to show" figures in the proposal was verified,
so the retest-due rule uses the date the person planned.

## Medicines and supplements

The medicines list (docs/body-design.md, "Medicines and supplements") gives each item substance
tags, worked out on the host. The review and the links read the tags of the items active on the
day of each draw. A tag explains a reading; the app never says to start, stop or change anything.

| Tag | What the app does with it | Source |
| --- | --- | --- |
| `biotin` | TSH, free T4 and free T3 are read with care on a draw taken while it is on the list, and a thyroid comparison with biotin at either draw is "cannot tell". The note says to tell whoever orders the test. No stop-before-test time is given: none was verified | Li 2017; FDA 2022 |
| `creatine` | Creatinine and eGFR are read with care; a change between a draw with and one without creatine is "cannot tell"; the creatinine and muscle rule names it | Antonio 2021 |
| `testosterone` | The haematocrit rule fires only with it on the list. No stop line is quoted (see "Not included") | none with a number |
| `thyroid`, `statin`, `iron`, `vitamin_d`, `estrogen`, `metformin`, `glp1`, `beta_blocker`, and the rest | Listed in the review under "taking then and now" so a change of medicine between two panels is visible. No link reads them yet | |

The review lists the items taken on each draw date with brand, product and tags, and the tags
that started or stopped between the two draws appear in `conditionsDiffer` as
`substance:<tag>` when a link reads that tag.

## Rules

Each rule reads the current panel (confirmed in the last 7 days), the one before it, the draw
conditions, the substances at each draw and the active trials. Severity follows the proposal.

| Kind | Fires when | Severity | Number and source |
| --- | --- | --- | --- |
| `trial_ready` | an active trial has a panel collected after its start that just came in, or its read date arrived (blood trials only once a panel after the start exists); 7 days | notice | none |
| `retest_due` | an active blood trial reached its read date and no panel has been taken since it started; 7 days | notice | the person's own date |
| `draw_conditions_differ` | the current and previous panel are known to differ on fasting, time of day, hard training, illness, biotin or creatine | notice | none |
| `apob_ldl_disagree` | ApoB above 90 (Mayo desirable) and LDL inside 100, or the reverse; or they moved in opposite directions by 15 % or more each | notice | lines from docs/lab-ranges.md; 15 % is the app's lab_trend line; Marston 2022 |
| `calculated_ldl_unreliable` | triglycerides 400 mg/dL or more with an LDL on the panel | notice | 400, Sampson 2020 |
| `ferritin_inflammation` | ferritin under 70 ng/mL with hs-CRP above its usual range | notice | 70, WHO 2020; the hs-CRP line is the reference table's |
| `high_ferritin_context` | ferritin above 300 (men and unset sex) or 200 (women), with triglycerides, HDL, hs-CRP and transferrin saturation (50 %) from the panel | clinician | Valenti 2023 |
| `thyroid_cholesterol` | TSH above range with LDL or total cholesterol above range or higher than last time | clinician | NICE NG238; Abreu 2017 |
| `free_testosterone_shbg` | SHBG outside its usual range with a total testosterone on the panel | notice | none: plain wording |
| `creatinine_muscle` | creatine on the list at the draw with creatinine above range or eGFR below it | notice | Antonio 2021 |
| `hematocrit_testosterone` | testosterone on the list at the draw and haematocrit above its usual range | clinician | none: plain wording |
| `expected_swing` | a marker with a verified CVi moved 15 % or more but within its RCV, under the same conditions; lab_trend holds its card for that marker | notice | the RCV table above |
| `lab_review_ready` | a current panel with a previous one: counts of each verdict | notice | the RCV table above |

Left out of the planned rules:

- **hs-CRP too high to trend** (above 10 mg/L, retest in about 2 weeks). The Pearson 2003 bands and
  the two-week repeat were not seen at the source (Circulation behind a bot check). The existing
  clinician tier for hs-CRP above 10 in docs/lab-ranges.md is unchanged.
- **Creatinine and muscle with lean mass.** No verified source ties a lean mass figure to eGFR, so
  the rule fires on creatine only.

## Not included

The proposal's map holds 63 links. These are not in the catalogue, because the source was seen
only in search results, the figure differed from our notes, or the link has no blood marker and
belongs to the wearable work:

Blood markers with blood markers: hs-CRP above 10 and the risk bands (Pearson 2003 not opened);
remnant cholesterol against risk (Quispe 2021, abstract only, not re-checked); albumin with
hs-CRP; TSH with creatinine; SHBG with metabolic health; SHBG with testosterone and the calculated
free testosterone (Vermeulen 1999, Bhasin 2018 not checked for this claim); haematocrit with
testosterone therapy (the 54 % line is in Bond 2024 but credited to the EAA and BSSM, and the
per-form rises were not found); creatinine with muscle mass (KDIGO 2024 not opened); vitamin D with
BMI and season; free T3 during a deficit; fructosamine with albumin and HbA1c; the TG to HDL
ratio; HbA1c with iron; FIB-4 and the steatosis indices.

Body and habits with blood: weight with triglycerides, LDL and HDL; weight with hs-CRP; weight with
ALT; exercise minutes with HDL; steps with triglycerides and HDL; fitness with long-term health;
short sleep with blood sugar; short sleep with testosterone; resting heart rate with thyroid; heavy
periods and tiredness with ferritin; overnight oxygen with haemoglobin; heart rate and HRV with
blood sugar and inflammation.

Before the draw: a meat meal with creatinine (Nair 2014 not in the checked set); a hard strength
session with AST, ALT and CK (Pettersson 2008 not in the checked set); a long endurance event with
CK and CRP; the day after a hard effort and posture with haemoglobin; cycle phase with lipids and
hs-CRP; the pill with binding proteins; recent illness or a vaccine with ferritin, LDL and
hs-CRP; alcohol with GGT; a short night with cortisol and TSH; very low carbohydrate eating with
LDL; altitude with haemoglobin; a biotin stop-before-test time (FDA 2019 text not readable).

Wearable and body signals (no blood marker; verified sources such as Natarajan 2021 for breathing
rate and Lambe 2025 for watch VO2 max are kept for that work): cycle phase with temperature,
resting heart rate, breathing and HRV; the temperature shift after ovulation; contraception and
the monthly swing; resting heart rate with season and week; breathing rate with illness; travel
with sleep; altitude with oxygen and heart rate; weight with the cycle; VO2 max with weight; VO2
max with resting heart rate and steps; blood pressure with weight, alcohol, exercise and salt;
sleep regularity; wrist oxygen against an oximeter; steps and sleep with mood.

Medicines and events: GLP-1 medicines, decongestants, ADHD stimulants, antidepressants, beta
blockers and vaccines with heart rate and HRV.
