using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria.ModLoader;

namespace AnimFramework.Animation
{
    // --- JSON schema (see the "Sheet Authoring Convention" section of ANIMFRAMEWORK_SPEC.md) ---

    internal class SheetDefinition
    {
        public string TexturePath { get; set; }
        public int FrameWidth { get; set; }
        public int FrameHeight { get; set; }
        public List<SheetRowDefinition> Rows { get; set; }
        public List<SheetRowDefinition> Overlays { get; set; }

        /// <summary>
        /// Sheet-wide default origin, used by any row/overlay that doesn't declare its own
        /// <see cref="SheetRowDefinition.OriginX"/>/<see cref="SheetRowDefinition.OriginY"/> —
        /// see <see cref="SheetLayoutLoader.ResolveOrigin"/> for the full row-then-sheet-then-
        /// computed-center cascade. Optional; both must be set together or neither applies,
        /// same rule as the per-row fields. Useful when most/all rows in a sheet share the same
        /// non-center pivot, so it doesn't need repeating on every row.
        /// </summary>
        public float? OriginX { get; set; }
        public float? OriginY { get; set; }
    }

    internal class SheetRowDefinition
    {
        public string State { get; set; }
        public int Row { get; set; }
        public int FrameCount { get; set; }
        public float? Fps { get; set; }
        public float? Duration { get; set; }
        public string Loop { get; set; }

        /// <summary>
        /// Per-row frame size override — falls back to <see cref="SheetDefinition.FrameWidth"/>/
        /// <see cref="SheetDefinition.FrameHeight"/> when unset (see
        /// <see cref="SheetLayoutLoader.ResolveDimension"/>). Lets one state (e.g. a Walking row
        /// with a wide-swinging sleeve) use a bigger frame without forcing every other state in
        /// the same sheet to pay for that width too.
        ///
        /// <see cref="FrameWidth"/> is always safe to vary per-row — each row's horizontal frame
        /// math (<c>frameIndex * FrameWidth</c>, in <see cref="AnimationClip.GetSourceRect"/>) is
        /// self-contained and doesn't affect any other row.
        ///
        /// <see cref="FrameHeight"/> is NOT self-contained the same way: rows are stacked
        /// vertically as <c>Row * FrameHeight</c> (that clip's own height) in the same method. A
        /// row declaring a taller FrameHeight than the sheet default will only read the correct
        /// pixels if the sheet's PNG was physically laid out to match — this is not validated or
        /// protected against here, it's on the author to arrange the sheet correctly when using a
        /// non-default FrameHeight.
        /// </summary>
        public int? FrameWidth { get; set; }
        public int? FrameHeight { get; set; }
        public float? OriginX { get; set; }
        public float? OriginY { get; set; }

        /// <summary>
        /// Bounds (seconds) for a timed-trigger feature reading this clip, e.g. Blink's own
        /// internal clock — see <see cref="AnimationClip.IntervalMin"/>/<c>IntervalMax</c> for
        /// the fallback rule when one or both are left unset. Meaningless (and ignored) for
        /// rows/overlays nothing timer-driven reads.
        /// </summary>
        public float? IntervalMin { get; set; }
        public float? IntervalMax { get; set; }

        /// <summary>
        /// Bounds, in completed loops, for a loop-count-triggered feature reading this clip —
        /// currently only the Idle-paired Blink state's own trigger (see
        /// <see cref="AnimationClip.LoopCountMin"/>/<c>LoopCountMax</c> for the fallback rule).
        /// A different unit from <see cref="IntervalMin"/>/<see cref="IntervalMax"/> above (loops,
        /// not seconds) — deliberately a separate pair of fields rather than overloading those.
        /// Meaningless (and ignored) for rows/overlays nothing loop-count-driven reads.
        /// </summary>
        public int? LoopCountMin { get; set; }
        public int? LoopCountMax { get; set; }
    }

    /// <summary>
    /// Loads a bundled sheet-layout JSON file into an <see cref="AnimationSet"/>. Defensive by
    /// design: hand-authored JSON is where mistakes actually happen (see spec's
    /// Fallback/Validation Needs section), so bad rows are logged and skipped rather than
    /// crashing the whole content mod's load. A missing/unreadable texture path is the one
    /// exception left loud — it throws, on purpose, so it's caught immediately at load time
    /// instead of leaving a silently invisible NPC with no clue why.
    /// </summary>
    public static class SheetLayoutLoader
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        public static AnimationSet Load(Mod mod, string jsonAssetPath)
        {
            byte[] bytes = mod.GetFileBytes(jsonAssetPath);
            SheetDefinition def = JsonSerializer.Deserialize<SheetDefinition>(bytes, JsonOptions);

            if (def == null || string.IsNullOrEmpty(def.TexturePath))
                throw new InvalidOperationException(
                    $"AnimFramework: '{jsonAssetPath}' is missing or has no texturePath.");

            // Loaded (and kept loaded, ImmediateLoad) up front purely to read its real pixel
            // dimensions for the row-bounds checks below; AnimationClip.GetTexture() requests
            // the same path again later for drawing, which tModLoader's asset system dedupes.
            Asset<Texture2D> texture = ModContent.Request<Texture2D>(def.TexturePath, AssetRequestMode.ImmediateLoad);
            int sheetWidth = texture.Value.Width;
            int sheetHeight = texture.Value.Height;

            var states = new Dictionary<string, AnimationClip>();
            foreach (SheetRowDefinition row in def.Rows ?? new List<SheetRowDefinition>())
            {
                AnimationClip clip = BuildClip(mod, jsonAssetPath, def, row, sheetWidth, sheetHeight);
                if (clip != null)
                    states[row.State] = clip;
            }

            var overlays = new List<AnimationClip>();
            foreach (SheetRowDefinition row in def.Overlays ?? new List<SheetRowDefinition>())
            {
                AnimationClip clip = BuildClip(mod, jsonAssetPath, def, row, sheetWidth, sheetHeight);
                if (clip != null)
                    overlays.Add(clip);
            }

            return new AnimationSet(states, overlays);
        }

        private static AnimationClip BuildClip(Mod mod, string jsonAssetPath, SheetDefinition def, SheetRowDefinition row, int sheetWidth, int sheetHeight)
        {
            if (string.IsNullOrEmpty(row.State) || row.FrameCount <= 0)
            {
                mod.Logger.Warn($"AnimFramework: '{jsonAssetPath}' has a row with no state name or frameCount <= 0 — skipped.");
                return null;
            }

            int frameWidth = ResolveDimension(row.FrameWidth, def.FrameWidth, mod, jsonAssetPath, row.State, "frameWidth");
            int frameHeight = ResolveDimension(row.FrameHeight, def.FrameHeight, mod, jsonAssetPath, row.State, "frameHeight");

            // The nastiest failure mode per spec: a row past the sheet's actual pixel bounds
            // doesn't crash, it silently reads garbage/out-of-bounds pixels. Bounds-check both
            // axes explicitly instead of letting that happen quietly — height catches a bad
            // `row` index (or an oversized per-row frameHeight override), width catches a
            // `frameCount` wider than the sheet actually is (rows are free to use fewer frames
            // than the sheet's widest row, e.g. a 2-frame Attacking row sharing a sheet with an
            // 8-frame Walking row, so this checks against the sheet's real width, not some other
            // row's frameCount). Uses the resolved per-row dimensions, not the sheet defaults, so
            // diagnostics reflect what this row actually reads.
            int rowBottom = (row.Row + 1) * frameHeight;
            if (rowBottom > sheetHeight)
            {
                mod.Logger.Error($"AnimFramework: '{jsonAssetPath}' row '{row.State}' (row {row.Row}) reads past the sheet's height ({rowBottom}px > {sheetHeight}px) — skipped.");
                return null;
            }

            int rowRight = row.FrameCount * frameWidth;
            if (rowRight > sheetWidth)
            {
                mod.Logger.Error($"AnimFramework: '{jsonAssetPath}' row '{row.State}' declares {row.FrameCount} frames wider than the sheet ({rowRight}px > {sheetWidth}px) — skipped.");
                return null;
            }

            LoopMode loop;
            if (!Enum.TryParse(row.Loop, true, out loop))
            {
                if (!string.IsNullOrEmpty(row.Loop))
                    mod.Logger.Warn($"AnimFramework: '{jsonAssetPath}' row '{row.State}' has unrecognized loop mode '{row.Loop}' — defaulting to Loop.");
                loop = LoopMode.Loop;
            }

            Vector2? origin = ResolveOrigin(row, def);

            if (row.IntervalMin.HasValue && row.IntervalMax.HasValue && row.IntervalMin.Value > row.IntervalMax.Value)
                mod.Logger.Warn($"AnimFramework: '{jsonAssetPath}' row '{row.State}' has intervalMin > intervalMax — swapped rather than treated as an error.");

            if (row.LoopCountMin.HasValue && row.LoopCountMax.HasValue && row.LoopCountMin.Value > row.LoopCountMax.Value)
                mod.Logger.Warn($"AnimFramework: '{jsonAssetPath}' row '{row.State}' has loopCountMin > loopCountMax — swapped rather than treated as an error.");

            return new AnimationClip(
                row.State, def.TexturePath, frameWidth, frameHeight, row.FrameCount,
                row.Row, row.Fps ?? 0f, row.Duration ?? 0f, loop, origin,
                row.IntervalMin, row.IntervalMax,
                row.LoopCountMin, row.LoopCountMax);
        }

        /// <summary>
        /// Resolves a row's effective frame dimension: the row's own override if it declared a
        /// usable (&gt; 0) value, otherwise the sheet-wide default. A declared-but-invalid
        /// (&lt;= 0) override degrades to the sheet default with a warning rather than crashing
        /// or skipping the whole row — same "never crash on bad JSON" spirit as the loop-mode
        /// fallback above.
        /// </summary>
        private static int ResolveDimension(int? rowValue, int sheetDefault, Mod mod, string jsonAssetPath, string state, string fieldName)
        {
            if (!rowValue.HasValue)
                return sheetDefault;

            if (rowValue.Value > 0)
                return rowValue.Value;

            mod.Logger.Warn($"AnimFramework: '{jsonAssetPath}' row '{state}' has {fieldName} <= 0 — using the sheet default instead.");
            return sheetDefault;
        }

        /// <summary>
        /// Resolves a row's effective origin: the row's own <c>originX</c>/<c>originY</c> if both
        /// are declared, else the sheet-wide default if both are declared there, else null so
        /// <see cref="AnimationClip.GetOrigin"/>'s own computed frame-center fallback applies.
        /// Same "both X and Y must be declared together" rule enforced at each tier — a lone
        /// coordinate at either level is ignored, not partially applied.
        /// </summary>
        private static Vector2? ResolveOrigin(SheetRowDefinition row, SheetDefinition def)
        {
            if (row.OriginX.HasValue && row.OriginY.HasValue)
                return new Vector2(row.OriginX.Value, row.OriginY.Value);

            if (def.OriginX.HasValue && def.OriginY.HasValue)
                return new Vector2(def.OriginX.Value, def.OriginY.Value);

            return null;
        }
    }
}
