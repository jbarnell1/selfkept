# Body and goals research

The sources behind every guidance figure the body package (`docs/body-design.md`) quotes in its
copy or its code comments. Each one below was opened and the figure seen on the page; the row ids
(E1, E2 and so on) are those of the 10 November 2026 figure check. Nothing here is a threshold the
app sets of its own: the smoothing, the outlier screen, the pace bands and the input ranges are
engineering choices and are listed at the end as such.

## Figures with a source

| Figure in the app | Where it is used | Source | What the source says |
| --- | --- | --- | --- |
| Lose about 0.5 to 1 % of body weight a week to hold on to muscle | `GoalSuggestions.LossShareLow`, `LossShareHigh` and the `lose_fat` why text; `LossRateFastRule` (fires above 1 %) and its sentence | Helms 2014, J Int Soc Sports Nutr 11:20. https://pmc.ncbi.nlm.nih.gov/articles/PMC4033492/ (E3) | Calories set to give "bodyweight losses of approximately 0.5 to 1%/wk to maximize muscle retention". A narrative review for natural bodybuilders preparing for a contest, so the copy says it was "written for lean, trained people". |
| Protein: more stopped adding muscle at about 1.6 g per kg of body weight a day | `GoalSuggestions.ProteinGramsPerKg`, the protein why text and the suggested Protein target (weight x 1.6, rounded to 5 g) | Morton 2018, Br J Sports Med 52(6):376-384. https://pmc.ncbi.nlm.nih.gov/articles/PMC5867436/ (E2) | 49 RCTs, 1,863 participants. Gains in fat-free mass plateau "at 1.62 g protein/kg/day (95% CI: 1.03 to 2.20)". The app rounds 1.62 to 1.6 and calls it a pooled analysis of training studies. |
| Adults: 7 or more hours of sleep a night | `GoalSuggestions.SleepMinutes` (420 min), the sleep why text and the suggested Sleep target | Watson 2015, Sleep 38(6):843-844 (AASM and SRS consensus). https://pmc.ncbi.nlm.nih.gov/articles/PMC4434546/ (E1) | "Adults should sleep 7 or more hours per night on a regular basis to promote optimal health." The copy calls it sleep researchers' consensus. |
| Waist to height 0.5 to 0.59 is increased central adiposity, 0.6 or more is high | `WaistHeightRule.Increased`, `High` and its sentence | NICE NG246, recommendation 1.9.14. https://www.nice.org.uk/guidance/ng246/chapter/Identifying-and-assessing-overweight-obesity-and-central-adiposity (E5a) | Healthy central adiposity 0.4 to 0.49; increased 0.5 to 0.59; high 0.6 or more; for both sexes and all ethnicities. |
| Keep your waist to less than half your height | `WaistHeightRule` sentence | NICE NG246, recommendation 1.9.15, same page (E5a) | Tell people to "keep their waist to less than half their height (so a waist-to-height ratio of under 0.5)". |
| The waist bands apply below a BMI of 35 | `WaistHeightRule.BmiLimit`: quiet at a BMI of 35 or more | NICE NG246, recommendation 1.9.14, same page (E5a) | The bands apply to people with a BMI under 35 kg/m2. |
| Estimated one-rep max: weight x (1 + reps / 30) | `StrengthMath.EstimatedMaxKg`, `estimatedMaxKg` and `bestEstimatedMaxKg` | Macarilla 2022, J Hum Kinet 83:131-141. https://pmc.ncbi.nlm.nih.gov/articles/PMC9465738/ (E7) | Writes the Epley equation as 1RM = load (kg) x (1 + 0.0333 x reps), citing a 2014 thesis. That equals weight x (1 + reps / 30) to rounding. Seen through this secondary citation; Epley's 1985 original was not opened. |
| A bathroom scale's body fat is a rough guide | `BodyMath.LeanShareOfLoss` comment and the lean share of loss | Frija-Masson 2021, JMIR mHealth uHealth 9(4):e22487. https://pmc.ncbi.nlm.nih.gov/articles/PMC8122302/ (E6a) | Median fat mass error against DXA of -2.2 to -4.4 kg across three consumer scales; "Smart scales are not accurate for body composition and should not replace DEXA in patient care." The app quotes no number from it, only that the reading is rough. |

## Choices with no source

These are engineering choices or plain defaults. The copy does not attribute them to anyone.

- The suggested Lifting days target, at least 2 a week (`GoalSuggestions.LiftingDaysPerWeek`).
- The trend's 10 % pull for a one-day gap, and 1 - 0.9^g for a gap of g days.
- The weigh-in screen: set aside more than 6 % from the median of up to 3 neighbours on each
  side, only with 4 readings or more.
- The weekly rate: 28 days, at least 6 readings over at least 14 days.
- Pace status: reached within 0.5 % (1 % for a hold-steady goal), flat at 0.05 % of the current
  value a week, ahead at 125 % of the planned pace, on track at 60 %, a projection only up to
  156 weeks.
- Lean share of loss needs at least 2.0 kg gone.
- The tally: a weekly average needs 3 of 7 days; a weekly "at most" from the day log needs 3
  logged days.
- Input ranges: weight 20 to 400 kg, body fat 2 to 75 %, waist 30 to 250 cm, height 90 to 250 cm,
  strength weight 0 to 1000 kg, reps 1 to 100, RPE 1 to 10.
- `goal_off_course` checks from two weeks in, on the goal's start weekday; a reading is fresh on
  its day and the day after.

## Figures without an opened source

- The 12-rep limit on the Epley estimate (`StrengthMath.MaxRepsForEstimate`). The code comment and
  the `StrengthSetDto` comment say the estimate "stops being useful" above twelve reps. No source
  for that cut-off was opened; it should either be worded as an engineering choice or be given a
  source.
- Epley 1985, the original publication of the formula. The formula is seen only through the
  secondary citation in Macarilla 2022 (E7).
- Ashwell 2012 as a source for the 0.5 waist to height boundary. The abstract does not state it
  (E5b); the app cites NICE NG246 instead and should keep doing so.
- Day-to-day repeatability of a consumer scale's body fat reading (E6b). The app makes no claim
  about it; anything that does would need a source first.
- Garthe 2011, sometimes cited next to Helms 2014 for the rate of loss, was not opened (E3). The
  app does not cite it.
