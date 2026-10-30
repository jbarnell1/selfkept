# Lab reference ranges

Approximate adult population reference ranges for the 30 markers in the monthly Rythm Health panel. These are not the lab's own ranges. Reference intervals vary by laboratory, assay, age, and (for hormones) time of day and menstrual phase, so the app shades these as a "usual range" behind a person's own values and labels them as approximate. Where a source used other units, the value was converted and the conversion is stated in the note. Values marked "optimal" are guideline or commonly cited treatment thresholds, not population ranges, and are labelled as such. Sources were opened on 2026-10-29.

Conventions: Low and High are the usual-range bounds in the app's stored unit. A blank Low means the source gives only an upper bound (lower is fine). "Good direction" is up (higher is better), down (lower is better), or range (stay inside the band). Where sexes differ there is one row per sex; "both" means a single adult range.

## Reference table

| Key | Unit | Sex | Low | High | Optimal or note | Good direction | Source |
|---|---|---|---|---|---|---|---|
| lab.total_testosterone | ng/dL | male | 240 | 950 | Mayo, age 19+. MedlinePlus gives 249 to 836 (17 to 50 y) and 193 to 740 (50+). Morning draw. | range | [Mayo TTFB](https://www.mayocliniclabs.com/test-catalog/overview/83686), [MedlinePlus](https://medlineplus.gov/ency/article/003707.htm) |
| lab.total_testosterone | ng/dL | female | 8 | 60 | Mayo, age 19+. MedlinePlus 8.4 to 48.1 (17 to 50 y). | range | [Mayo TTFB](https://www.mayocliniclabs.com/test-catalog/overview/83686), [MedlinePlus](https://medlineplus.gov/ency/article/003707.htm) |
| lab.free_testosterone | pg/mL | male | 35 | 207 | Converted from Mayo ng/dL x 10 (equilibrium dialysis). Mayo is age banded: 5.25 to 20.7 ng/dL at 20 to 24 y down to 3.47 to 13.0 ng/dL at 65 to 69 y; the row spans ages 20 to 70. Direct immunoassays report much lower numbers, so assay matters. | range | [Mayo TTFB](https://www.mayocliniclabs.com/test-catalog/overview/83686) |
| lab.free_testosterone | pg/mL | female |  | 10.8 | Converted from Mayo <0.13 to 1.08 ng/dL (age 20 to 24; upper bound falls to 0.84 ng/dL by 65 to 69 y). Assay floor is 1.3 pg/mL. | range | [Mayo TTFB](https://www.mayocliniclabs.com/test-catalog/overview/83686) |
| lab.estradiol | pg/mL | male | 10 | 40 | Mayo adult male. MedlinePlus 10 to 50. | range | [Mayo EEST](https://www.mayocliniclabs.com/test-catalog/overview/81816), [MedlinePlus](https://medlineplus.gov/ency/article/003711.htm) |
| lab.estradiol | pg/mL | female | 15 | 350 | Mayo premenopausal, all phases; postmenopausal <10. By phase (Labcorp ECLIA): follicular 12.5 to 166, ovulation 85.8 to 498, luteal 43.8 to 211, postmenopausal <6 to 54.7. | range | [Mayo EEST](https://www.mayocliniclabs.com/test-catalog/overview/81816), [Labcorp 004515](https://www.labcorp.com/tests/004515/estradiol) |
| lab.progesterone | ng/mL | male |  | 0.2 | Mayo adult male <0.20. MedlinePlus <1. | range | [Mayo PGSN](https://www.mayocliniclabs.com/test-catalog/overview/8141), [MedlinePlus](https://medlineplus.gov/ency/article/003714.htm) |
| lab.progesterone | ng/mL | female |  | 24 | Phase dependent (Mayo): follicular <=0.89, ovulation <=12, luteal 1.8 to 24, postmenopausal <=0.20. Row bounds cover the full cycle; day 21 to 23 value >10 indicates ovulation. | range | [Mayo PGSN](https://www.mayocliniclabs.com/test-catalog/overview/8141), [MedlinePlus](https://medlineplus.gov/ency/article/003714.htm) |
| lab.shbg | nmol/L | male | 13.3 | 89.5 | Mayo, age 18+. | range | [Mayo SHBG1](https://www.mayocliniclabs.com/test-catalog/overview/608102) |
| lab.shbg | nmol/L | female | 18.2 | 135.5 | Mayo, 18 to 46 y; postmenopausal 16.8 to 125.2. | range | [Mayo SHBG1](https://www.mayocliniclabs.com/test-catalog/overview/608102) |
| lab.cortisol | ug/dL | both | 7 | 25 | Morning (a.m.) serum, Mayo age 18+ (p.m. 2 to 14). MedlinePlus 8 a.m. 5 to 25. Unit mcg/dL = ug/dL. | range | [Mayo CORT](https://www.mayocliniclabs.com/test-catalog/overview/8545), [MedlinePlus](https://medlineplus.gov/ency/article/003693.htm) |
| lab.tsh | mIU/L | both | 0.3 | 4.2 | Mayo, age 20+. MedlinePlus 0.4 to 4.8 (uIU/mL = mIU/L). | range | [Mayo STSH](https://www.mayocliniclabs.com/test-catalog/overview/8939), [MedlinePlus](https://medlineplus.gov/ency/article/003684.htm) |
| lab.free_t3 | pg/mL | both | 2.0 | 4.4 | Mayo, age 19+. MedlinePlus 210 to 440 pg/dL, converted (divide by 100) to 2.1 to 4.4 pg/mL. | range | [Mayo T3FR](https://www.mayocliniclabs.com/test-catalog/overview/621321), [MedlinePlus](https://medlineplus.gov/ency/article/003687.htm) |
| lab.free_t4 | ng/dL | both | 0.8 | 2.0 | Mayo (dialysis). MedlinePlus 0.8 to 1.9. | range | [Mayo FRT4D](https://www.mayocliniclabs.com/test-catalog/overview/8859), [MedlinePlus](https://medlineplus.gov/ency/article/003517.htm) |
| lab.apob | mg/dL | male | 66 | 133 | Conventional reference range (Cleveland Clinic). Optimal (labelled, not a population range): Mayo "desirable" <90; 90 to 99 above desirable, 100 to 119 borderline high, 120 to 139 high, >=140 very high. Commonly cited lower target <80 (Cleveland Clinic cites <65 or <80 for statin-treated adults 40 to 75). Below 48 is very low. | down | [Cleveland Clinic](https://my.clevelandclinic.org/health/diagnostics/24992-apolipoprotein-b-test), [Mayo APOLB](https://www.mayocliniclabs.com/test-catalog/overview/624438) |
| lab.apob | mg/dL | female | 60 | 117 | Same thresholds as male row: desirable <90, commonly cited optimal <80, >130 higher risk. | down | [Cleveland Clinic](https://my.clevelandclinic.org/health/diagnostics/24992-apolipoprotein-b-test), [Mayo APOLB](https://www.mayocliniclabs.com/test-catalog/overview/624438) |
| lab.ldl | mg/dL | both |  | 100 | Categories: <100 optimal, 100 to 129 near optimal, 130 to 159 borderline high, 160 to 189 high, >=190 very high. <70 for people with heart disease or several risk factors. | down | [Cleveland Clinic](https://my.clevelandclinic.org/health/articles/24391-ldl-cholesterol), [MedlinePlus](https://medlineplus.gov/cholesterollevelswhatyouneedtoknow.html) |
| lab.hdl | mg/dL | male | 40 | 80 | <40 low (MedlinePlus, Cleveland Clinic). 60 or higher is best; Cleveland Clinic notes above 80 may not be protective. | up | [MedlinePlus](https://medlineplus.gov/cholesterollevelswhatyouneedtoknow.html), [Cleveland Clinic](https://my.clevelandclinic.org/health/articles/11920-cholesterol-numbers-what-do-they-mean) |
| lab.hdl | mg/dL | female | 50 | 80 | <50 low. 60 or higher is best; above 80 uncertain benefit. | up | [MedlinePlus](https://medlineplus.gov/cholesterollevelswhatyouneedtoknow.html), [Cleveland Clinic](https://my.clevelandclinic.org/health/articles/11920-cholesterol-numbers-what-do-they-mean) |
| lab.ldl_apob_ratio | ratio | both |  |  | No established reference range. Derived value used only to spot discordance between LDL-C and ApoB (Mayo treats ApoB as a target analogous to LDL-C). Do not shade or flag. | range | [Mayo APOLB](https://www.mayocliniclabs.com/test-catalog/overview/624438) |
| lab.hscrp | mg/L | both |  | 3 | Cardiovascular risk bands (AHA/CDC 2003, as reproduced by Labcorp): <1 low, 1 to 3 average, >3 high. Mayo reference <2.0; Mayo Clinic treats >=2.0 as higher risk and 8 to 10 or more as high. Values >10 suggest acute inflammation or infection rather than cardiovascular risk; repeat after 2 weeks. | down | [Labcorp 120766](https://www.labcorp.com/tests/120766/c-reactive-protein-crp-high-sensitivity-cardiac-risk-assessment), [Mayo HSCRP](https://www.mayocliniclabs.com/test-catalog/overview/606909), [Mayo Clinic](https://www.mayoclinic.org/tests-procedures/c-reactive-protein-test/about/pac-20385228) |
| lab.total_cholesterol | mg/dL | both |  | 200 | <200 normal, 200 to 239 borderline high, >=240 high. | down | [MedlinePlus](https://medlineplus.gov/cholesterollevelswhatyouneedtoknow.html), [Cleveland Clinic](https://my.clevelandclinic.org/health/articles/11920-cholesterol-numbers-what-do-they-mean) |
| lab.remnant_cholesterol | mg/dL | both |  | 30 | Calculated: total cholesterol minus LDL-C minus HDL-C. No established normal range. Commonly cited cutoff: >=30 mg/dL (about 0.78 to 0.8 mmol/L) associated with higher ASCVD risk in PREDIMED and other cohorts. | down | [Wang 2024 review (PMC)](https://pmc.ncbi.nlm.nih.gov/articles/PMC11224847/), [Frontiers 2022 review (PMC)](https://pmc.ncbi.nlm.nih.gov/articles/PMC9621322/) |
| lab.tc_hdl_ratio | ratio | both |  | 5 | Total cholesterol divided by HDL. Goal below 5; below 3.5 considered very good. Mayo Clinic: higher ratio means higher risk, and non-HDL cholesterol (<130 mg/dL) is now preferred over the ratio. | down | [University Hospitals](https://www.uhhospitals.org/health-information/health-and-wellness-library/tests-and-procedures/article/lab-tests-v1/lipid-panel-with-total-cholesterol-hdl-ratio), [Mayo Clinic](https://www.mayoclinic.org/diseases-conditions/high-blood-cholesterol/expert-answers/cholesterol-ratio/faq-20058006) |
| lab.vitamin_d | ng/mL | both | 20 | 50 | 25-OH vitamin D. NIH ODS (Food and Nutrition Board): <12 deficient, 12 to <20 inadequate, >=20 sufficient. Mayo decision values: <10 severe deficiency, 10 to 19 mild to moderate deficiency, 20 to 50 optimum, 51 to 80 increased hypercalciuria risk, >80 toxicity possible. Many labs call 20 to 29 insufficient. | range | [NIH ODS](https://ods.od.nih.gov/factsheets/VitaminD-HealthProfessional/), [Mayo 25HDN](https://www.mayocliniclabs.com/test-catalog/overview/83670) |
| lab.ferritin | ng/mL | male | 31 | 409 | Mayo, age 18+ (mcg/L = ng/mL). MedlinePlus 30 to 400. Acute phase reactant: rises with inflammation. | range | [Mayo FERR1](https://www.mayocliniclabs.com/test-catalog/overview/619953), [MedlinePlus](https://medlineplus.gov/ency/article/003490.htm) |
| lab.ferritin | ng/mL | female | 6 | 175 | Mayo, 18 to 50 y; age 51+ is 11 to 328. MedlinePlus 13 to 150. Lower values within range still suggest low iron stores. | range | [Mayo FERR1](https://www.mayocliniclabs.com/test-catalog/overview/619953), [MedlinePlus](https://medlineplus.gov/ency/article/003490.htm) |
| lab.fructosamine | umol/L | both | 200 | 285 | Mayo (mcmol/L = umol/L). Reflects average glucose over the prior 1 to 3 weeks; high values mean poor glycemic control. Low albumin lowers the result. | range | [Mayo FRUCT](https://www.mayocliniclabs.com/test-catalog/overview/81610) |
| lab.albumin | g/dL | both | 3.4 | 5.4 | MedlinePlus. | range | [MedlinePlus](https://medlineplus.gov/ency/article/003480.htm) |
| lab.triglycerides | mg/dL | both |  | 150 | Fasting. <150 normal, 150 to 199 borderline high, 200 to 499 high, >=500 very high. | down | [MedlinePlus](https://medlineplus.gov/triglycerides.html) |
| lab.tg_hdl_ratio | ratio | male |  | 2.8 | No guideline range. Insulin resistance surrogate; 2024 systematic review reports average cutoffs of about 2.8 (men) and 2.5 (women) with mg/dL inputs, lower (about 2.0 men, 1.5 women) in African American populations. | down | [Biomedicines 2024 review (PMC)](https://pmc.ncbi.nlm.nih.gov/articles/PMC11274455/) |
| lab.tg_hdl_ratio | ratio | female |  | 2.5 | See male row. | down | [Biomedicines 2024 review (PMC)](https://pmc.ncbi.nlm.nih.gov/articles/PMC11274455/) |
| lab.uric_acid | mg/dL | male | 4.0 | 8.6 | MedlinePlus. Gout treatment goal <6 (or <5). | range | [MedlinePlus](https://medlineplus.gov/ency/article/003476.htm) |
| lab.uric_acid | mg/dL | female | 3.0 | 7.1 | MedlinePlus. Gout treatment goal <6 (or <5). | range | [MedlinePlus](https://medlineplus.gov/ency/article/003476.htm) |
| lab.hematocrit | % | male | 37 | 48 | MedlinePlus. | range | [MedlinePlus](https://medlineplus.gov/ency/article/003646.htm) |
| lab.hematocrit | % | female | 34 | 43 | MedlinePlus. | range | [MedlinePlus](https://medlineplus.gov/ency/article/003646.htm) |
| lab.hemoglobin | g/dL | male | 13.8 | 17.2 | MedlinePlus. | range | [MedlinePlus](https://medlineplus.gov/ency/article/003645.htm) |
| lab.hemoglobin | g/dL | female | 12.1 | 15.1 | MedlinePlus. | range | [MedlinePlus](https://medlineplus.gov/ency/article/003645.htm) |
| lab.creatinine | mg/dL | male | 0.7 | 1.3 | MedlinePlus. Tracks muscle mass; interpret with eGFR. | range | [MedlinePlus](https://medlineplus.gov/ency/article/003475.htm) |
| lab.creatinine | mg/dL | female | 0.5 | 0.95 | MedlinePlus. | range | [MedlinePlus](https://medlineplus.gov/ency/article/003475.htm) |
| lab.alp | U/L | both | 40 | 129 | MedlinePlus (IU/L = U/L); no separate adult sex ranges given. Higher in pregnancy and growing children. | range | [MedlinePlus](https://medlineplus.gov/ency/article/003470.htm) |
| lab.ggt | U/L | male | 8 | 61 | Mayo, age 18+. MedlinePlus adult 5 to 40 (no sex split). Alcohol and some drugs raise it. | down | [Mayo GGT](https://www.mayocliniclabs.com/test-catalog/overview/8677), [MedlinePlus](https://medlineplus.gov/ency/article/003458.htm) |
| lab.ggt | U/L | female | 5 | 36 | Mayo, age 18+. | down | [Mayo GGT](https://www.mayocliniclabs.com/test-catalog/overview/8677), [MedlinePlus](https://medlineplus.gov/ency/article/003458.htm) |
| lab.egfr | mL/min/1.73m2 | both | 90 |  | CKD stages (NKF): G1 >=90 normal, G2 60 to 89 mild loss, G3a 45 to 59, G3b 30 to 44, G4 15 to 29, G5 <15 kidney failure. Declines with age; G1 and G2 are only CKD with other kidney damage for 3+ months. NKF pages do not print the unit; mL/min/1.73m2 is the standard reporting unit. | up | [NKF eGFR](https://www.kidney.org/kidney-topics/estimated-glomerular-filtration-rate-egfr), [NKF CKD stages](https://www.kidney.org/kidney-topics/stages-chronic-kidney-disease-ckd) |

## Flag tiers

Watch tier: out of range, show the value with the usual range and suggest rechecking next month. Clinician tier: out of range in a way that should prompt a "talk to a clinician" prompt. Thresholds below are the clinician trigger; anything else out of range on that marker is watch tier.

| Key | Tier | Trigger | Rationale |
|---|---|---|---|
| lab.tsh | clinician | <0.1 or >10 mIU/L | Far outside range suggests overt hyper- or hypothyroidism rather than lab noise; 4.2 to 10 is watch tier (subclinical, recheck). |
| lab.free_t4 | clinician | <0.8 or >2.0 ng/dL | Mayo interprets these bounds as possible hypo- or hyperthyroidism; should be read together with TSH. |
| lab.egfr | clinician | <60 | G3a or worse; sustained for 3 months defines CKD stage 3. 60 to 89 is watch tier. |
| lab.creatinine | clinician | above range | Rising creatinine is the raw signal behind a falling eGFR; a single high value warrants review. |
| lab.hemoglobin | clinician | below range | Anemia needs a cause found (iron, bleeding, B12, marrow). High values are watch tier. |
| lab.hematocrit | clinician | below range | Same reasoning as hemoglobin; both move together. |
| lab.hscrp | clinician | >10 mg/L | Above 10 suggests acute infection or inflammation, not cardiovascular risk; 3 to 10 is watch tier. |
| lab.fructosamine | clinician | above range | High fructosamine means 1 to 3 weeks of elevated average glucose; it is the panel's only glycemic marker. |
| lab.cortisol | clinician | morning <5 or >25 ug/dL | Low morning cortisol can indicate adrenal insufficiency; very high values need a cause. |
| lab.albumin | clinician | below range | Low albumin reflects liver, kidney, or nutritional problems and also distorts fructosamine. |
| lab.ferritin | clinician | <15 or >1000 ng/mL | Below 15 is iron deficiency by most definitions; above 1000 suggests overload, liver disease, or significant inflammation. Other out-of-range values are watch tier. |
| lab.vitamin_d | clinician | <12 or >80 ng/mL | <12 is deficiency by the NIH definition; >80 is the lowest level Mayo associates with toxicity. 12 to 20 and 50 to 80 are watch tier. |
| lab.triglycerides | clinician | >=500 mg/dL | Very high triglycerides carry pancreatitis risk and change how LDL is measured. 150 to 499 is watch tier. |
| lab.ldl | clinician | >=190 mg/dL | Very high LDL raises the question of familial hypercholesterolemia and usually means medication. |
| lab.uric_acid | clinician | >9 mg/dL | Marked hyperuricemia with or without gout symptoms merits review. |
| lab.total_testosterone | watch | out of range | Hormones swing with time of day, sleep, and illness; confirm on repeat before acting. |
| lab.free_testosterone | watch | out of range | Assay dependent and age dependent; recheck with total testosterone and SHBG. |
| lab.estradiol | watch | out of range | Varies widely by cycle phase and menopause status. |
| lab.progesterone | watch | out of range | Interpretation depends entirely on cycle timing. |
| lab.shbg | watch | out of range | Modifies free hormone levels; not acute. |
| lab.free_t3 | watch | out of range | Not a sensitive marker on its own; read with TSH and free T4. |
| lab.apob | watch | above optimal | Long-horizon cardiovascular risk; a trend, not an emergency. |
| lab.hdl | watch | below low bound | Risk marker with limited direct treatment; context from the rest of the lipid panel matters. |
| lab.ldl_apob_ratio | none | not flagged | No established range. |
| lab.total_cholesterol | watch | above 200 | Interpreted through LDL, HDL, and ApoB rather than on its own. |
| lab.remnant_cholesterol | watch | above 30 | Derived value with no standard range; trend only. |
| lab.tc_hdl_ratio | watch | above 5 | Derived value; non-HDL and ApoB are preferred. |
| lab.tg_hdl_ratio | watch | above cutoff | Insulin resistance surrogate without a guideline range. |
| lab.alp | watch | out of range | Mild elevations are common and need GGT and liver tests for context. |
| lab.ggt | watch | above range | Rises with alcohol and many drugs; recheck before escalating. |

Potassium, sodium, and glucose are not on the panel, so no acute electrolyte or glycemic emergency tiers are defined here.

## Sources

All pages opened on 2026-10-29.

- [MedlinePlus Medical Encyclopedia: Hemoglobin](https://medlineplus.gov/ency/article/003645.htm)
- [MedlinePlus Medical Encyclopedia: Hematocrit](https://medlineplus.gov/ency/article/003646.htm)
- [MedlinePlus Medical Encyclopedia: Creatinine blood test](https://medlineplus.gov/ency/article/003475.htm)
- [MedlinePlus Medical Encyclopedia: ALP blood test](https://medlineplus.gov/ency/article/003470.htm)
- [MedlinePlus Medical Encyclopedia: Gamma-glutamyl transferase (GGT) blood test](https://medlineplus.gov/ency/article/003458.htm)
- [MedlinePlus Medical Encyclopedia: Uric acid, blood](https://medlineplus.gov/ency/article/003476.htm)
- [MedlinePlus Medical Encyclopedia: Albumin blood (serum) test](https://medlineplus.gov/ency/article/003480.htm)
- [MedlinePlus Medical Encyclopedia: Ferritin blood test](https://medlineplus.gov/ency/article/003490.htm)
- [MedlinePlus Medical Encyclopedia: Cortisol blood test](https://medlineplus.gov/ency/article/003693.htm)
- [MedlinePlus Medical Encyclopedia: Testosterone](https://medlineplus.gov/ency/article/003707.htm)
- [MedlinePlus Medical Encyclopedia: Estradiol blood test](https://medlineplus.gov/ency/article/003711.htm)
- [MedlinePlus Medical Encyclopedia: Serum progesterone](https://medlineplus.gov/ency/article/003714.htm)
- [MedlinePlus Medical Encyclopedia: TSH test](https://medlineplus.gov/ency/article/003684.htm)
- [MedlinePlus Medical Encyclopedia: Free T4 test](https://medlineplus.gov/ency/article/003517.htm)
- [MedlinePlus Medical Encyclopedia: T3 test](https://medlineplus.gov/ency/article/003687.htm)
- [MedlinePlus: Cholesterol Levels: What You Need to Know](https://medlineplus.gov/cholesterollevelswhatyouneedtoknow.html)
- [MedlinePlus: Triglycerides](https://medlineplus.gov/triglycerides.html)
- [MedlinePlus Medical Test: C-Reactive Protein (CRP) Test](https://medlineplus.gov/lab-tests/c-reactive-protein-crp-test/) (opened; gives no hs-CRP bands)
- [MedlinePlus Medical Test: Vitamin D Test](https://medlineplus.gov/lab-tests/vitamin-d-test/) (opened; gives no numeric cutoffs)
- [NIH Office of Dietary Supplements: Vitamin D, Health Professional Fact Sheet](https://ods.od.nih.gov/factsheets/VitaminD-HealthProfessional/)
- [National Kidney Foundation: Estimated Glomerular Filtration Rate (eGFR)](https://www.kidney.org/kidney-topics/estimated-glomerular-filtration-rate-egfr)
- [National Kidney Foundation: Stages of Chronic Kidney Disease (CKD)](https://www.kidney.org/kidney-topics/stages-chronic-kidney-disease-ckd)
- [Mayo Clinic Laboratories TTFB: Testosterone, Total, Bioavailable, and Free, Serum](https://www.mayocliniclabs.com/test-catalog/overview/83686)
- [Mayo Clinic Laboratories EEST: Estradiol, Serum](https://www.mayocliniclabs.com/test-catalog/overview/81816)
- [Mayo Clinic Laboratories PGSN: Progesterone, Serum](https://www.mayocliniclabs.com/test-catalog/overview/8141)
- [Mayo Clinic Laboratories SHBG1: Sex Hormone-Binding Globulin, Serum](https://www.mayocliniclabs.com/test-catalog/overview/608102)
- [Mayo Clinic Laboratories STSH: Thyroid-Stimulating Hormone-Sensitive, Serum](https://www.mayocliniclabs.com/test-catalog/overview/8939)
- [Mayo Clinic Laboratories FRT4D: T4 (Thyroxine), Free, Dialysis, Serum](https://www.mayocliniclabs.com/test-catalog/overview/8859)
- [Mayo Clinic Laboratories T3FR: T3 (Triiodothyronine), Free, Serum](https://www.mayocliniclabs.com/test-catalog/overview/621321)
- [Mayo Clinic Laboratories APOLB: Apolipoprotein B, Serum](https://www.mayocliniclabs.com/test-catalog/overview/624438)
- [Mayo Clinic Laboratories CORT: Cortisol, Serum](https://www.mayocliniclabs.com/test-catalog/overview/8545)
- [Mayo Clinic Laboratories FERR1: Ferritin, Serum](https://www.mayocliniclabs.com/test-catalog/overview/619953)
- [Mayo Clinic Laboratories GGT: Gamma-Glutamyltransferase, Serum](https://www.mayocliniclabs.com/test-catalog/overview/8677)
- [Mayo Clinic Laboratories HSCRP: C-Reactive Protein, High Sensitivity, Serum](https://www.mayocliniclabs.com/test-catalog/overview/606909)
- [Mayo Clinic Laboratories FRUCT: Fructosamine, Serum](https://www.mayocliniclabs.com/test-catalog/overview/81610)
- [Mayo Clinic Laboratories 25HDN: 25-Hydroxyvitamin D2 and D3, Serum](https://www.mayocliniclabs.com/test-catalog/overview/83670)
- [Mayo Clinic: Cholesterol ratio or non-HDL cholesterol: Which is most important?](https://www.mayoclinic.org/diseases-conditions/high-blood-cholesterol/expert-answers/cholesterol-ratio/faq-20058006)
- [Mayo Clinic: C-reactive protein test](https://www.mayoclinic.org/tests-procedures/c-reactive-protein-test/about/pac-20385228)
- [Cleveland Clinic: LDL Cholesterol](https://my.clevelandclinic.org/health/articles/24391-ldl-cholesterol)
- [Cleveland Clinic: Cholesterol Numbers: What Do They Mean](https://my.clevelandclinic.org/health/articles/11920-cholesterol-numbers-what-do-they-mean)
- [Cleveland Clinic: Apolipoprotein B (APOB) Test](https://my.clevelandclinic.org/health/diagnostics/24992-apolipoprotein-b-test)
- [Cleveland Clinic: C-Reactive Protein (CRP) Test](https://my.clevelandclinic.org/health/diagnostics/23056-c-reactive-protein-crp-test) (opened; standard CRP in mg/dL only, not used in the table)
- [Cleveland Clinic: Vitamin D Deficiency](https://my.clevelandclinic.org/health/diseases/15050-vitamin-d-vitamin-d-deficiency) (opened; no numeric cutoffs, not used in the table)
- [University Hospitals: Lipid Panel With Total Cholesterol: HDL Ratio](https://www.uhhospitals.org/health-information/health-and-wellness-library/tests-and-procedures/article/lab-tests-v1/lipid-panel-with-total-cholesterol-hdl-ratio)
- [University of Rochester Medical Center Health Encyclopedia: Estradiol (Blood)](https://www.urmc.rochester.edu/encyclopedia/content?contenttypeid=167&contentid=estradiol) (opened; no phase ranges, not used in the table)
- [Labcorp 004515: Estradiol](https://www.labcorp.com/tests/004515/estradiol)
- [Labcorp 120766: C-Reactive Protein (CRP), High Sensitivity (Cardiac Risk Assessment)](https://www.labcorp.com/tests/120766/c-reactive-protein-crp-high-sensitivity-cardiac-risk-assessment), which reproduces the AHA/CDC 2003 risk bands (Pearson et al., Circulation 2003;107:499-511). The AHA journal page itself was behind a bot check and could not be opened.
- [Wang et al. 2024, A significant presence in atherosclerotic cardiovascular disease: Remnant cholesterol: A review, Medicine (PMC11224847)](https://pmc.ncbi.nlm.nih.gov/articles/PMC11224847/)
- [Remnant cholesterol and atherosclerotic cardiovascular disease: Metabolism, mechanism, evidence, and treatment, Frontiers in Cardiovascular Medicine 2022 (PMC9621322)](https://pmc.ncbi.nlm.nih.gov/articles/PMC9621322/)
- [The Triglyceride/HDL Ratio as a Surrogate Biomarker for Insulin Resistance, Biomedicines 2024 (PMC11274455)](https://pmc.ncbi.nlm.nih.gov/articles/PMC11274455/)

## Machine-readable

Columns: key, unit, sex, low, high, optimal_low, optimal_high, good_direction, tier, source_url. Blank means no bound or no established value. Tier is the flag tier for an out-of-range value on that row (clinician triggers are listed in the flag tiers table above).

```csv
key,unit,sex,low,high,optimal_low,optimal_high,good_direction,tier,source_url
lab.total_testosterone,ng/dL,male,240,950,,,range,watch,https://www.mayocliniclabs.com/test-catalog/overview/83686
lab.total_testosterone,ng/dL,female,8,60,,,range,watch,https://www.mayocliniclabs.com/test-catalog/overview/83686
lab.free_testosterone,pg/mL,male,35,207,,,range,watch,https://www.mayocliniclabs.com/test-catalog/overview/83686
lab.free_testosterone,pg/mL,female,,10.8,,,range,watch,https://www.mayocliniclabs.com/test-catalog/overview/83686
lab.estradiol,pg/mL,male,10,40,,,range,watch,https://www.mayocliniclabs.com/test-catalog/overview/81816
lab.estradiol,pg/mL,female,15,350,,,range,watch,https://www.mayocliniclabs.com/test-catalog/overview/81816
lab.progesterone,ng/mL,male,,0.2,,,range,watch,https://www.mayocliniclabs.com/test-catalog/overview/8141
lab.progesterone,ng/mL,female,,24,,,range,watch,https://www.mayocliniclabs.com/test-catalog/overview/8141
lab.shbg,nmol/L,male,13.3,89.5,,,range,watch,https://www.mayocliniclabs.com/test-catalog/overview/608102
lab.shbg,nmol/L,female,18.2,135.5,,,range,watch,https://www.mayocliniclabs.com/test-catalog/overview/608102
lab.cortisol,ug/dL,both,7,25,,,range,clinician,https://www.mayocliniclabs.com/test-catalog/overview/8545
lab.tsh,mIU/L,both,0.3,4.2,,,range,clinician,https://www.mayocliniclabs.com/test-catalog/overview/8939
lab.free_t3,pg/mL,both,2.0,4.4,,,range,watch,https://www.mayocliniclabs.com/test-catalog/overview/621321
lab.free_t4,ng/dL,both,0.8,2.0,,,range,clinician,https://www.mayocliniclabs.com/test-catalog/overview/8859
lab.apob,mg/dL,male,66,133,,90,down,watch,https://my.clevelandclinic.org/health/diagnostics/24992-apolipoprotein-b-test
lab.apob,mg/dL,female,60,117,,90,down,watch,https://my.clevelandclinic.org/health/diagnostics/24992-apolipoprotein-b-test
lab.ldl,mg/dL,both,,100,,100,down,watch,https://my.clevelandclinic.org/health/articles/24391-ldl-cholesterol
lab.hdl,mg/dL,male,40,80,60,80,up,watch,https://medlineplus.gov/cholesterollevelswhatyouneedtoknow.html
lab.hdl,mg/dL,female,50,80,60,80,up,watch,https://medlineplus.gov/cholesterollevelswhatyouneedtoknow.html
lab.ldl_apob_ratio,ratio,both,,,,,range,none,https://www.mayocliniclabs.com/test-catalog/overview/624438
lab.hscrp,mg/L,both,,3,,1,down,watch,https://www.labcorp.com/tests/120766/c-reactive-protein-crp-high-sensitivity-cardiac-risk-assessment
lab.total_cholesterol,mg/dL,both,,200,,200,down,watch,https://medlineplus.gov/cholesterollevelswhatyouneedtoknow.html
lab.remnant_cholesterol,mg/dL,both,,30,,30,down,watch,https://pmc.ncbi.nlm.nih.gov/articles/PMC11224847/
lab.tc_hdl_ratio,ratio,both,,5,,3.5,down,watch,https://www.uhhospitals.org/health-information/health-and-wellness-library/tests-and-procedures/article/lab-tests-v1/lipid-panel-with-total-cholesterol-hdl-ratio
lab.vitamin_d,ng/mL,both,20,50,20,50,range,watch,https://www.mayocliniclabs.com/test-catalog/overview/83670
lab.ferritin,ng/mL,male,31,409,,,range,watch,https://www.mayocliniclabs.com/test-catalog/overview/619953
lab.ferritin,ng/mL,female,6,175,,,range,watch,https://www.mayocliniclabs.com/test-catalog/overview/619953
lab.fructosamine,umol/L,both,200,285,,,range,clinician,https://www.mayocliniclabs.com/test-catalog/overview/81610
lab.albumin,g/dL,both,3.4,5.4,,,range,clinician,https://medlineplus.gov/ency/article/003480.htm
lab.triglycerides,mg/dL,both,,150,,150,down,watch,https://medlineplus.gov/triglycerides.html
lab.tg_hdl_ratio,ratio,male,,2.8,,,down,watch,https://pmc.ncbi.nlm.nih.gov/articles/PMC11274455/
lab.tg_hdl_ratio,ratio,female,,2.5,,,down,watch,https://pmc.ncbi.nlm.nih.gov/articles/PMC11274455/
lab.uric_acid,mg/dL,male,4.0,8.6,,,range,watch,https://medlineplus.gov/ency/article/003476.htm
lab.uric_acid,mg/dL,female,3.0,7.1,,,range,watch,https://medlineplus.gov/ency/article/003476.htm
lab.hematocrit,%,male,37,48,,,range,clinician,https://medlineplus.gov/ency/article/003646.htm
lab.hematocrit,%,female,34,43,,,range,clinician,https://medlineplus.gov/ency/article/003646.htm
lab.hemoglobin,g/dL,male,13.8,17.2,,,range,clinician,https://medlineplus.gov/ency/article/003645.htm
lab.hemoglobin,g/dL,female,12.1,15.1,,,range,clinician,https://medlineplus.gov/ency/article/003645.htm
lab.creatinine,mg/dL,male,0.7,1.3,,,range,clinician,https://medlineplus.gov/ency/article/003475.htm
lab.creatinine,mg/dL,female,0.5,0.95,,,range,clinician,https://medlineplus.gov/ency/article/003475.htm
lab.alp,U/L,both,40,129,,,range,watch,https://medlineplus.gov/ency/article/003470.htm
lab.ggt,U/L,male,8,61,,,down,watch,https://www.mayocliniclabs.com/test-catalog/overview/8677
lab.ggt,U/L,female,5,36,,,down,watch,https://www.mayocliniclabs.com/test-catalog/overview/8677
lab.egfr,mL/min/1.73m2,both,90,,90,,up,clinician,https://www.kidney.org/kidney-topics/estimated-glomerular-filtration-rate-egfr
```
