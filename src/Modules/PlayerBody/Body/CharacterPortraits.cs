using System;
using System.IO;
using UnityEngine;

namespace WildWasteland.Modules.PlayerBody
{
    internal static class CharacterPortraits
    {
        internal static readonly string[] Names = { "Female", "Male", "Merchant", "Bodyguard", "Sprokka", "Pistoleer", "Mechanic", "OrganicMechanic", "Gungirl", "Shoota" };
        internal static readonly int[] Order = { 1, 2, 3, 4, 7, 0, 8, 9 };
        static readonly Texture2D[] Pictures = new Texture2D[Names.Length];
        static readonly DateTime[] Modified = new DateTime[Names.Length];
        internal static int Canonical(int choice) { return choice == 5 ? 3 : choice == 6 ? 2 : choice; }
        internal static Texture Picture(int choice) { return Pictures[Canonical(choice)]; }

        // Only on opening settings: external Photoshop replacements reload without restarting the game.
        internal static void Prepare()
        {
            string directory = PlayerBodyModule.ModPath("Portraits");
            Directory.CreateDirectory(directory);
            for (int entry = 0; entry < Order.Length; entry++)
            {
                int i = Order[entry];
                string path = Path.Combine(directory, Names[i] + ".png");
                Texture2D picture = null;
                try
                {
                    if (!File.Exists(path))
                        using (var source = typeof(CharacterPortraits).Assembly.GetManifestResourceStream("PlayerBody.CharacterPortraits." + Names[i] + ".png"))
                            if (source != null) using (var output = File.Create(path)) source.CopyTo(output);
                    if (!File.Exists(path)) continue;
                    var modified = File.GetLastWriteTimeUtc(path);
                    if (Pictures[i] != null && Modified[i] == modified) continue;
                    picture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "WW portrait " + Names[i], hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
                    if (!ImageConversion.LoadImage(picture, File.ReadAllBytes(path), true)) throw new InvalidDataException("not a PNG");
                    if (Pictures[i] != null) UnityEngine.Object.Destroy(Pictures[i]);
                    Pictures[i] = picture; Modified[i] = modified;
                }
                catch (Exception e)
                {
                    if (picture != null) UnityEngine.Object.Destroy(picture);
                    PlayerBodyModule.Warn("Character portrait " + path + ": " + e.Message);
                }
            }
            InventoryModel.RefreshPortrait();
        }
    }
}
