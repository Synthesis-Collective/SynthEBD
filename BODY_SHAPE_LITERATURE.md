# Body-Shape Classification: Literature and SynthEBD's Measurement System

This document records how published research on female body-shape classification maps onto SynthEBD's
**Label by Measurements** system (Body Type Profiles), and what we learned when we applied that research to a corpus
of BodySlide presets. It is meant as a reference for anyone authoring or reviewing Shape rules (Pear, Hourglass,
Rectangle and so on), and as a record of why the measurements are built the way they are.

Compiled October 2026. All source content is paraphrased; see [References](#references) for full citations.

---

## 1. Why this matters

SynthEBD assigns body shapes to BodySlide presets by measuring each preset's mesh and evaluating rules against those
measurements. A Shape rule is only as good as its definition of the shape, and "pear", "hourglass" and "spoon" mean
different things in different traditions:

- **Stylist and fashion guides** describe shapes visually, often comparing shoulders with hips.
- **Apparel research**, chiefly the Female Figure Identification Technique (FFIT), defines shapes numerically from
  circumferences (bust, waist, high hip, hip) taken from 3D body scans.

Basing SynthEBD's rules on the published research makes them explainable and reproducible. Applying that research to
game bodies also exposes where human-population definitions stop working.

## 2. SynthEBD's measurement building blocks

A Body Type Profile measures each preset on the deformed body mesh, at each NPC weight. The measurement kinds relevant
here:

| Kind | What it measures | Relevant to |
|---|---|---|
| `PointDistance`, `AxisDistance`, `RatioDistance` | Distances between key vertices, or a ratio of two distances | Widths (shoulders, armpits, hips) and frame ratios |
| `RegionVolume` | Enclosed volume of a region (a box selecting a patch of the body surface, capped into a solid) | Fullness: cup size, belly, buttocks; band volumes |
| `Girth` | Tape-measure circumference of a region band | Bust, waist, high-hip and hip girths as defined in apparel research |
| `MeasurementRatio` | One measurement divided by another | Scale-free comparisons, for example bust girth / hip girth |

Two design points carry over from the literature:

- **Regions are defined on the zeroed ("Zeroed Sliders") mesh**, so a region selects the same anatomy on every preset
  however far the sliders move it. This plays the role of the anatomical landmark definitions in scanning standards.
- **A girth is the convex-hull perimeter of a horizontal slice.** A tape measure bridges concavities, such as the gap
  between the breasts or between the thighs, and a convex hull does the same. A `Girth` measurement slices its band at
  fixed zeroed-space heights and takes the largest slice (fullest bust or hips), the smallest (natural waist) or the
  mean. This mirrors how FFIT and sizing surveys locate the "fullest" and "narrowest" levels.

## 3. The sources

| Source | What it contributes | Access |
|---|---|---|
| Simmons, Istook & Devarajan 2004, FFIT Part I | The nine FFIT shapes, described qualitatively; a table of earlier stylist systems | Open (JTATM 4(1); archived copies) |
| Simmons, Istook & Devarajan 2004, FFIT Part II | The shape-sorting software, search order, development-sample distribution | Open (JTATM 4(1); archived copies) |
| Devarajan 2004 (NC State MS thesis; also Devarajan & Istook 2004, JTATM) | Validation on 531 SizeUSA women; per-shape group means; landmark problems; survey of pre-FFIT systems | Open |
| Lee, Istook, Nam & Park 2007 | The numeric FFIT formulas most later work quotes; US vs Korean comparison | Paywalled (abstract read; formulas via Parker 2021 and Sokolowski 2020) |
| Parker, Hayes, Brownbridge & Gill 2021 | Reliability study on 1,679 UK women: landmark definitions change the class of 22-40% of women | Open (author manuscript) |
| Sokolowski et al. 2020 (3DBODY.TECH) and Sokolowski & Bettencourt 2020 (ITAA) | Modified FFIT formulas for plus-size bodies | Open |
| Shin & Saeidi 2020 (ITAA), after Song & Ashdown 2011 | A data-driven alternative: choose features that are independent of overall size, then cluster | Open |
| Jung, Kang & Kim 2026 (Scientific Reports) | A fully ratio-based nine-shape system with data-derived thresholds | Open |
| Fish et al. 2013 (ITAA) | Uses FFIT only to select study models | Open; low relevance |

### 3.1 FFIT's origins (Simmons et al. 2004; Devarajan 2004)

- The two founding FFIT papers publish **no numeric thresholds**. The shapes are described in words. The numbers lived
  in the authors' software and first appeared in print in Lee et al. 2007.
- The thresholds came from **expert judgement** in apparel fit, not from statistics. The shape set itself grew
  iteratively: an initial five shapes left many of a 222-woman student sample unclassified, so shapes were added until
  every subject fitted one.
- FFIT's waist-definition constants (roughly 9-10 inches) trace back to stylist descriptions of the hourglass, in which
  the waist is about ten inches smaller than the bust and hips.
- The validation study found that even the curviest real-women groups average a **hip-to-waist difference of about
  11-12 inches**, and that **high hip / waist is the only ratio** among FFIT's features: about 1.12-1.15 for every shape
  except Spoon, at about 1.23.
- Pre-FFIT systems were visual or proportional: silhouette rating against a grid (Douty), multi-scale visual assessment
  (Connell's Body Shape Assessment Scale), and pattern-industry figure types. A few early proportional typologies were
  scale-free, but none used girth formulas.

### 3.2 The FFIT formulas (Lee et al. 2007, as quoted by Parker 2021 and Sokolowski 2020)

The seven-shape formulas compare girth **differences in inches**. Written in our own notation:

| Shape | Test (inches) |
|---|---|
| Hourglass | bust and hips within about 1-3.6 in of each other, and a defined waist (bust - waist >= 9 or hips - waist >= 10) |
| Bottom Hourglass | hips exceed bust by 3.6-10 in, hips - waist >= 9, high hip / waist < 1.193 |
| Top Hourglass | bust exceeds hips by 1-10 in, bust - waist >= 9 |
| Spoon | hips exceed bust by more than 2 in, hips - waist >= 7, high hip / waist > 1.193 |
| Triangle (pear) | hips exceed bust by at least 3.6 in, hips - waist < 9 |
| Inverted Triangle | bust exceeds hips by at least 3.6 in, and a weakly defined waist |
| Rectangle | bust and hips within 3.6 in, and no defined waist |

The published versions **disagree in detail**: AND versus OR in the Hourglass and Rectangle tests, which waist term
Inverted Triangle uses, and the order in which shapes are tested. FFIT assigns the **first** shape that matches, so the
order matters wherever a body satisfies more than one test. Lee et al. also report that the only US women they could
not classify had hips more than about 15.5 inches larger than the waist. That marks the edge of FFIT's design range.

### 3.3 Landmarks change the answer (Parker et al. 2021)

On 1,679 UK women, Parker et al. compared FFIT's own measurement definitions with the UK sizing survey's (SizeUK):

- **Hip:** FFIT takes the largest girth between waist and crotch; SizeUK takes the girth at maximum buttock projection
  in profile. **Neither extends below the crotch**, so thighs are excluded in both.
- **Waist:** both take the smallest girth in a band above the small of the back, but FFIT searches a taller band.
- **High hip:** both definitions sit between waist and hip, at different heights.

Switching definitions moved **22-40% of women to a different shape**. Spoon was by far the most fragile (as little as
1% agreement between definition sets), because it depends on the high-hip landmark. Rectangle and Inverted Triangle
were nearly stable.

Parker et al. also found that **shoulder width** separates the shapes clearly (narrowest for Spoon, widest for
Inverted Triangle), although FFIT ignores shoulders. Older stylist systems define pear, V and H shapes by shoulders
against hips.

### 3.4 Ratio-based and data-driven alternatives

- **Jung et al. 2026** rebuilt the nine shapes from ratios: bust/hip within about ±5% counts as balanced (tightened
  slightly by a decision tree), and a waist counts as **defined when it is under 85% of both bust and hip**. Thresholds
  come from the data distribution and are refined with a decision tree. Front-view girth ratios alone pushed
  belly-heavy bodies into Rectangle, so front-to-back depth measures were added to separate Oval and Diamond.
- **Shin & Saeidi 2020**, following Song & Ashdown 2011, keep only measurements that correlate weakly with overall size
  before clustering, so the shape classes describe shape rather than size.
- **Sokolowski et al. 2020** adjusted FFIT for plus-size bodies, adding guards for bodies whose waist is not the
  narrowest girth. No plus-size subject in their sample was Hourglass or Top Hourglass.

## 4. What we found applying this to BodySlide presets

The corpus is a cache of about 25,500 (preset, weight) rows of CBBE 3BA BodySlide presets. Girths were computed as
described in section 2 and converted to inches at roughly 0.5625 inches per game unit. Treat absolute inch values as
approximate; the ratios do not depend on that conversion.

1. **Literal FFIT finds almost no pears.** On true girths: Spoon 59.7%, Hourglass 33.4%, Top Hourglass 5.0%, Bottom
   Hourglass 1.7%, Triangle (pear) 2 rows. The bust-versus-hip test behaves sensibly (61% of rows have hips at least
   3.6 in larger than the bust). The waist test is what fails: FFIT's pear needs hips - waist under 9 in, but the corpus
   5th percentile is about 13 in and the median about 17.7 in.
2. **The corpus sits outside FFIT's design range.** A hip-to-waist difference of 13-18 inches is beyond the curviest
   real-women groups (about 11-12 in, Devarajan 2004) and around the point where Lee et al. could not classify bodies at
   all. The Spoon-heavy result is therefore an artifact of applying human-calibrated constants, not a finding about
   the presets.
3. **Ratio rules calibrated on humans fail the same way.** Under Jung et al.'s defined-waist rule (waist under 85% of
   both bust and hip), **99.8%** of rows have a defined waist. The median waist/hip girth ratio is about **0.59**
   (5th-95th percentile 0.49-0.68), against roughly 0.70-0.85 for real women. Thresholds for this corpus must come from
   its own distribution or from visual verdicts.
4. **Girth and volume are not interchangeable for tail categories.** Band-volume ratios rank presets similarly to girth
   ratios (Spearman 0.90 for bust/hip, 0.86 for waist/hip), but only 0.57 for high hip / waist. At strict cuts the two
   pick largely different sets: with the same quantile cuts, girth and volume agree on only 14-38% of the presets they
   call Pear. Volume integrates mass over the band's height (and squares girth differences), while a girth reads the
   fullest or narrowest level. This is why SynthEBD gained the `Girth` kind.
5. **Band placement matters on game meshes too.**
   - A bust band reaching up to the armpit lets the armpit fold into the slice, and a fullest-slice girth then picked
     the armpit level on about a quarter of sampled presets. Capping the bust girth band just below the armpit made every
     sampled preset peak at the bust.
   - The narrowest waist slice sat above a tight waist band on about 9% of sampled presets, so the waist girth band was
     extended upward, stopping short of the under-bust.
   - These echo Parker et al.'s finding that landmark definitions dominate the result.

## 5. Design implications for SynthEBD's Shape rules

1. **Use the literature for structure, not constants.** FFIT's choice of girths and comparisons (bust against hip,
   waist against both, high hip against waist) transfers well. Its inch thresholds do not.
2. **Prefer scale-free ratios** (`MeasurementRatio` over `Girth` measurements), so rules do not depend on the
   unit conversion or on overall body size.
3. **Calibrate cut points to the corpus**: by quantiles of the corpus distribution or, preferably, against visual
   verdicts on a judging set, in the manner of Devarajan's validation and Jung et al.'s decision trees.
4. **Document landmark choices**, and keep them fixed once rules are calibrated against them. In particular, decide
   whether the hip includes the upper thighs. The research definitions exclude them; a hip band that includes them
   follows the stylist description of a pear ("hips and thighs").
5. **Consider shoulders.** Shoulder-to-hip width is a strong shape signal in the data and the basis of stylist
   definitions. SynthEBD's earlier frame-based Pear rule (armpit width against hips) is closer to that tradition than to
   FFIT, and is kept as an alternative rule set.
6. **Avoid first-match ordering.** On this corpus most bodies satisfy several shape tests, so a scored or
   nearest-prototype assignment, or explicit margins, is more robust than FFIT's sequential order.
7. **Belly-dominant shapes (Oval, Diamond) need front-to-back depth**, not just front-view girths.
8. **Spoon is the least reliable shape**, in both the literature (landmark sensitivity) and our data (weak
   volume-girth agreement). Rules for it deserve extra visual validation.

## References

1. Simmons, K., Istook, C. L., & Devarajan, P. (2004). Female Figure Identification Technique (FFIT) for apparel.
   Part I: Describing female shapes. *Journal of Textile and Apparel, Technology and Management*, 4(1).
2. Simmons, K., Istook, C. L., & Devarajan, P. (2004). Female Figure Identification Technique (FFIT) for apparel.
   Part II: Development of shape sorting software. *Journal of Textile and Apparel, Technology and Management*, 4(1).
3. Devarajan, P. (2004). *Validation of "Female Figure Identification Technique (FFIT) for Apparel" Methodology*
   (Master's thesis). North Carolina State University.
   <https://repository.lib.ncsu.edu/items/35fb969e-9394-4307-8799-e42f957e76b8>. Also: Devarajan, P., & Istook, C. L.
   (2004), *JTATM*, 4(1).
4. Lee, J. Y., Istook, C. L., Nam, Y. J., & Park, S. M. (2007). Comparison of body shape between USA and Korean women.
   *International Journal of Clothing Science and Technology*, 19(5), 374-391.
   <https://doi.org/10.1108/09556220710819555>
5. Parker, C. J., Hayes, S. G., Brownbridge, K., & Gill, S. (2021). Assessing the female figure identification
   technique's reliability as a body shape classification system. *Ergonomics*, 64(8), 1035-1051.
   Author manuscript:
   <https://repository.mmu.ac.uk/articles/journal_contribution/Assessing_the_female_figure_identification_technique_s_reliability_as_a_body_shape_classification_system/32460303>
6. Sokolowski, S. L., et al. (2020). Modification of the Female Figure Identification Technique (FFIT) formulas to
   include plus size bodies. *Proceedings of 3DBODY.TECH 2020*.
   <https://proc.3dbody.tech/papers/2020/2022sokolowski.pdf>
7. Sokolowski, S. L., & Bettencourt, C. (2020). How understanding female plus size body shapes throughout a size range
   can affect apparel grading and design attributes. *International Textile and Apparel Association Annual Conference
   Proceedings*, 77. <https://www.iastatedigitalpress.com/itaa/article/11894/galley/11265/view/>
8. Shin, E., & Saeidi, E. (2020). Fit analysis of ASTM size standards for overweight and obese women in the United
   States based on body shapes. *International Textile and Apparel Association Annual Conference Proceedings*, 77.
   <https://iastatedigitalpress.com/itaa/article/12179/galley/11442/view>
9. Song & Ashdown (2011), a data-driven body-shape categorization of adult women. Known here only as cited by
   Shin & Saeidi 2020 (reference 8); not read directly.
10. Jung, Kang, & Kim (2026). Automatic classification of female body shape using 3D anthropometric scan data.
    *Scientific Reports*, 16, 22963. <https://www.nature.com/articles/s41598-026-37807-1>
11. Fish, MacGillivray, An, & Beard (2013). Shapewear fit and attractiveness ratings based on body type.
    *International Textile and Apparel Association Annual Conference Proceedings*, 70.
    <https://iastatedigitalpress.com/itaa/article/2204/galley/2077/view>

Older works known here only through the summaries in references 1 and 3: Douty's visual somatometry (1960s), Connell
et al.'s Body Shape Assessment Scale, and Rasband's *Fabulous Fit* (1994).
