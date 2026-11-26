using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria.ModLoader;

namespace AnimFramework.Animation
{
    // --- JSON schema (see "Sheet Authoring Convention" in ANIMFRAMEWORK_SPEC.md) ---

    internal class SheetDefinition
    {
        public string TexturePath { get; set; }

        /// <summary>Optional sheet for the town NPC's shimmered variant. Must match the main sheet's pixel size.</summary>
        public string ShimmerTexturePath { get; set; }

        public int FrameWidth { get; set; }
        public int FrameHeight { get; set; }
        public List<SheetRowDefinition> Rows { get; set; }
        public List<SheetRowDefinition> Overlays { get; set; }

        /// <summary>Sheet-wide default origin for rows that don't declare one. X and Y must be set together.</summary>
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
        /// Per-row frame size override. Width is always safe to vary. Height is not: rows are
        /// located at <c>Row * FrameHeight</c>, so a taller row only reads the right pixels if
        /// the PNG was laid out to match. That isn't validated.
        /// </summary>
        public int? FrameWidth { get; set; }
        public int? FrameHeight { get; set; }
        public float? OriginX { get; set; }
        public float? OriginY { get; set; }

        /// <summary>Seconds between firings, for timer-driven features (Blink overlay, SpecialIdle).</summary>
        public float? IntervalMin { get; set; }
        public float? IntervalMax { get; set; }

        /// <summary>Completed loops between firings, for loop-count-driven features (IdleBlink).</summary>
        public int? LoopCountMin { get; set; }
        public int? LoopCountMax { get; set; }
    }

    /// <summary>
    /// Loads a sheet-layout JSON into an <see cref="AnimationSet"/>. Bad rows are logged and
    /// skipped. A missing JSON or texture throws; <see cref="AnimationRegistry"/> and
    /// <see cref="AnimatedNPC"/> catch that, log it once, and fall back to vanilla drawing.
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
                throw new InvalidOperationException($"AnimFramework: '{jsonAssetPath}' is missing or has no texturePath.");

            // Loaded now to read the real pixel size for the bounds checks below. Later draw-time
            // requests for the same path are deduplicated by tModLoader's asset system.
            Texture2D texture = ModContent.Request<Texture2D>(def.TexturePath, AssetRequestMode.ImmediateLoad).Value;
            int sheetWidth = texture.Width;
            int sheetHeight = texture.Height;

            string shimmerPath = ValidateShimmerSheet(mod, jsonAssetPath, def, sheetWidth, sheetHeight);

            var states = new Dictionary<string, AnimationClip>();
            foreach (SheetRowDefinition row in def.Rows ?? new List<SheetRowDefinition>())
            {
                AnimationClip clip = BuildClip(mod, jsonAssetPath, def, shimmerPath, row, sheetWidth, sheetHeight);
                if (clip != null)
                    states[row.State] = clip;
            }

            var overlays = new List<AnimationClip>();
            foreach (SheetRowDefinition row in def.Overlays ?? new List<SheetRowDefinition>())
            {
                AnimationClip clip = BuildClip(mod, jsonAssetPath, def, shimmerPath, row, sheetWidth, sheetHeight);
                if (clip != null)
                    overlays.Add(clip);
            }

            return new AnimationSet(states, overlays);
        }

        /// <summary>Returns the shimmer sheet path if declared and usable, otherwise null (normal sheet used for both variants).</summary>
        private static string ValidateShimmerSheet(Mod mod, string jsonAssetPath, SheetDefinition def, int sheetWidth, int sheetHeight)
        {
            if (string.IsNullOrEmpty(def.ShimmerTexturePath))
                return null;

            if (!ModContent.HasAsset(def.ShimmerTexturePath))
            {
                mod.Logger.Warn($"AnimFramework: '{jsonAssetPath}' shimmerTexturePath '{def.ShimmerTexturePath}' was not found; using the normal sheet for shimmered NPCs.");
                return null;
            }

            Texture2D shimmer = ModContent.Request<Texture2D>(def.ShimmerTexturePath, AssetRequestMode.ImmediateLoad).Value;
            if (shimmer.Width != sheetWidth || shimmer.Height != sheetHeight)
            {
                mod.Logger.Warn($"AnimFramework: '{jsonAssetPath}' shimmer sheet is {shimmer.Width}x{shimmer.Height}px but the main sheet is {sheetWidth}x{sheetHeight}px; using the normal sheet for shimmered NPCs.");
                return null;
            }

            return def.ShimmerTexturePath;
        }

        private static AnimationClip BuildClip(Mod mod, string jsonAssetPath, SheetDefinition def, string shimmerPath, SheetRowDefinition row, int sheetWidth, int sheetHeight)
        {
            if (string.IsNullOrEmpty(row.State) || row.FrameCount <= 0)
            {
                mod.Logger.Warn($"AnimFramework: '{jsonAssetPath}' has a row with no state name or frameCount <= 0; skipped.");
                return null;
            }

            int frameWidth = ResolveDimension(row.FrameWidth, def.FrameWidth, mod, jsonAssetPath, row.State, "frameWidth");
            int frameHeight = ResolveDimension(row.FrameHeight, def.FrameHeight, mod, jsonAssetPath, row.State, "frameHeight");

            // A row outside the sheet wouldn't crash; it would silently draw garbage. Check both axes.
            int rowBottom = (row.Row + 1) * frameHeight;
            if (rowBottom > sheetHeight)
            {
                mod.Logger.Error($"AnimFramework: '{jsonAssetPath}' row '{row.State}' (row {row.Row}) reads past the sheet's height ({rowBottom}px > {sheetHeight}px); skipped.");
                return null;
            }

            int rowRight = row.FrameCount * frameWidth;
            if (rowRight > sheetWidth)
            {
                mod.Logger.Error($"AnimFramework: '{jsonAssetPath}' row '{row.State}' declares {row.FrameCount} frames, wider than the sheet ({rowRight}px > {sheetWidth}px); skipped.");
                return null;
            }

            LoopMode loop;
            if (!Enum.TryParse(row.Loop, true, out loop))
            {
                if (!string.IsNullOrEmpty(row.Loop))
                    mod.Logger.Warn($"AnimFramework: '{jsonAssetPath}' row '{row.State}' has unrecognized loop mode '{row.Loop}'; defaulting to Loop.");
                loop = LoopMode.Loop;
            }

            if (row.IntervalMin > row.IntervalMax)
                mod.Logger.Warn($"AnimFramework: '{jsonAssetPath}' row '{row.State}' has intervalMin > intervalMax; swapped.");

            if (row.LoopCountMin > row.LoopCountMax)
                mod.Logger.Warn($"AnimFramework: '{jsonAssetPath}' row '{row.State}' has loopCountMin > loopCountMax; swapped.");

            return new AnimationClip(
                row.State, def.TexturePath, frameWidth, frameHeight, row.FrameCount,
                row.Row, row.Fps ?? 0f, row.Duration ?? 0f, loop, ResolveOrigin(row, def),
                row.IntervalMin, row.IntervalMax,
                row.LoopCountMin, row.LoopCountMax,
                shimmerPath);
        }

        /// <summary>The row's override if it's &gt; 0, otherwise the sheet default (warning on an invalid override).</summary>
        private static int ResolveDimension(int? rowValue, int sheetDefault, Mod mod, string jsonAssetPath, string state, string fieldName)
        {
            if (!rowValue.HasValue)
                return sheetDefault;

            if (rowValue.Value > 0)
                return rowValue.Value;

            mod.Logger.Warn($"AnimFramework: '{jsonAssetPath}' row '{state}' has {fieldName} <= 0; using the sheet default.");
            return sheetDefault;
        }

        /// <summary>Row origin, else sheet origin, else null (frame center). X and Y must be declared together at each level.</summary>
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
