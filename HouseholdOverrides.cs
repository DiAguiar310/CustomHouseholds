using System;
using System.Collections.Generic;
using System.IO;
using Colossal.PSI.Environment;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CustomHouseholds
{
    [Serializable]
    public class HouseholdOverrideEntry
    {
        public string prefabName = "";
        public int level = -1;
        public int households;
        public bool captured;
        public int originalCount = -1;
        public float originalSpaceMultiplier = 1f;

        public string GetKey()
        {
            return $"{prefabName}#{level}";
        }
    }

    [Serializable]
    public class PendingRestore
    {
        public string prefabName = "";
        public int level = -1;
        public int originalCount = -1;
        public float originalSpaceMultiplier = 1f;

        public PendingRestore()
        {
        }

        public PendingRestore(HouseholdOverrideEntry entry)
        {
            prefabName = entry.prefabName;
            level = entry.level;
            originalCount = entry.originalCount;
            originalSpaceMultiplier = entry.originalSpaceMultiplier;
        }

        public string GetKey()
        {
            return $"{prefabName}#{level}";
        }
    }

    [Serializable]
    public class OriginalSnapshot
    {
        public int count = -1;
        public float spaceMultiplier = 1f;
    }

    public static class HouseholdOverrides
    {
        public static readonly string DataFolder = Path.Combine(EnvPath.kUserDataPath, "ModsData", "CustomHouseholds");
        public static readonly string FilePath = Path.Combine(DataFolder, "overrides.json");
        public static readonly string PrefabListPath = Path.Combine(DataFolder, "prefab_list.txt");
        public static readonly string ReadmePath = Path.Combine(DataFolder, "LEIA-ME.txt");
        public static readonly string OriginalsPath = Path.Combine(DataFolder, "originals.json");

        public static Dictionary<string, HouseholdOverrideEntry> Entries = new Dictionary<string, HouseholdOverrideEntry>();
        public static Dictionary<string, OriginalSnapshot> KnownOriginals = new Dictionary<string, OriginalSnapshot>();
        public static readonly List<PendingRestore> PendingRestores = new List<PendingRestore>();

        public static int Version { get; private set; }

        public static bool DumpRequested;

        public static long LastSeenWriteTicks;

        private const string ReadmeText = @"CustomHouseholds - como usar
==============================

1) Carregue uma cidade. O mod gera a lista de predios nesta pasta:
   prefab_list.txt  ->  nome do prefab#nivel = familias atuais.

2) Crie o arquivo overrides.json nesta pasta (ele e criado vazio automaticamente).
   Use exatamente os nomes do prefab_list.txt. Dois formatos aceitos:

   {
     ""Nome do Prefab#2"": 10,
     ""Outro Prefab#3"": { ""households"": 6 }
   }

   O numero e a quantidade de familias (unidades residenciais) desejada.
   O #N e o nivel do predio (0 a 5), ou -1 quando nao se aplica.

3) Salve o arquivo. Com o jogo rodando (despausado), o mod detecta a mudanca
   sozinho em alguns segundos e aplica em TODOS os predios iguais da cidade,
   inclusive nos que forem construidos depois.

4) O mod memoriza o valor original de cada predio na primeira vez que o edita
   (originals.json). O botao Reset restaura sempre esse original, mesmo depois
   de sair e voltar ao jogo.

5) Para desfazer: apague a entrada e salve (os valores originais voltam),
   ou use o botao 'Limpar todos os overrides' nas opcoes do mod.

6) Botoes nas opcoes do mod (Menu Principal ou in-game > Opcoes > mods):
   exportar lista de predios, recarregar overrides do arquivo, limpar tudo.
";

        public static void Load()
        {
            try
            {
                Directory.CreateDirectory(DataFolder);
                EnsureReadme();
                LoadOriginals();

                if (!File.Exists(FilePath))
                {
                    File.WriteAllText(FilePath, "{" + Environment.NewLine + "}" + Environment.NewLine);
                }

                string text = File.ReadAllText(FilePath);
                var raw = JsonConvert.DeserializeObject<Dictionary<string, JToken>>(text)
                    ?? new Dictionary<string, JToken>();

                var fixedEntries = new Dictionary<string, HouseholdOverrideEntry>();
                foreach (var pair in raw)
                {
                    var entry = ParseEntry(pair.Key, pair.Value);
                    if (entry != null)
                    {
                        fixedEntries[entry.GetKey()] = entry;
                    }
                }

                foreach (var pair in Entries)
                {
                    if (!fixedEntries.ContainsKey(pair.Key) && pair.Value.captured)
                    {
                        PendingRestores.Add(new PendingRestore(pair.Value));
                    }
                }

                bool originalsChanged = false;
                foreach (var pair in fixedEntries)
                {
                    if (pair.Value.captured && pair.Value.originalCount > 0 && !KnownOriginals.ContainsKey(pair.Key))
                    {
                        KnownOriginals[pair.Key] = new OriginalSnapshot
                        {
                            count = pair.Value.originalCount,
                            spaceMultiplier = pair.Value.originalSpaceMultiplier == 0f ? 1f : pair.Value.originalSpaceMultiplier,
                        };
                        originalsChanged = true;
                    }
                }

                if (originalsChanged)
                {
                    SaveOriginals();
                }

                Entries = fixedEntries;
                LastSeenWriteTicks = File.GetLastWriteTimeUtc(FilePath).Ticks;
            }
            catch (Exception e)
            {
                Mod.log.Error($"Failed to load overrides from {FilePath}: {e}");

                try
                {
                    if (File.Exists(FilePath))
                    {
                        LastSeenWriteTicks = File.GetLastWriteTimeUtc(FilePath).Ticks;
                    }
                }
                catch
                {
                }
            }

            Version++;
            Mod.log.Info($"Overrides loaded: {Entries.Count} entries.");
        }

        private static HouseholdOverrideEntry ParseEntry(string key, JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            HouseholdOverrideEntry entry;

            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            {
                entry = new HouseholdOverrideEntry
                {
                    households = token.Value<int>(),
                };
            }
            else if (token.Type == JTokenType.Object)
            {
                entry = token.ToObject<HouseholdOverrideEntry>();
                if (entry == null)
                {
                    return null;
                }
            }
            else
            {
                return null;
            }

            if (string.IsNullOrEmpty(entry.prefabName))
            {
                int hash = key.LastIndexOf('#');
                if (hash > 0)
                {
                    entry.prefabName = key.Substring(0, hash);
                    int level;
                    if (int.TryParse(key.Substring(hash + 1), out level))
                    {
                        entry.level = level;
                    }
                }
                else
                {
                    entry.prefabName = key;
                }
            }

            return entry;
        }

        private static void EnsureReadme()
        {
            try
            {
                if (!File.Exists(ReadmePath) || File.ReadAllText(ReadmePath) != ReadmeText)
                {
                    File.WriteAllText(ReadmePath, ReadmeText);
                }
            }
            catch
            {
            }
        }

        private static void LoadOriginals()
        {
            try
            {
                if (File.Exists(OriginalsPath))
                {
                    KnownOriginals = JsonConvert.DeserializeObject<Dictionary<string, OriginalSnapshot>>(File.ReadAllText(OriginalsPath))
                        ?? new Dictionary<string, OriginalSnapshot>();
                }
            }
            catch (Exception e)
            {
                Mod.log.Warn($"Failed to load originals from {OriginalsPath}: {e.Message}");
            }
        }

        private static void SaveOriginals()
        {
            try
            {
                Directory.CreateDirectory(DataFolder);
                File.WriteAllText(OriginalsPath, JsonConvert.SerializeObject(KnownOriginals, Formatting.Indented));
            }
            catch (Exception e)
            {
                Mod.log.Error($"Failed to save originals to {OriginalsPath}: {e}");
            }
        }

        public static bool TryGetKnownOriginal(string key, out int count, out float spaceMultiplier)
        {
            count = -1;
            spaceMultiplier = 1f;

            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            if (KnownOriginals.TryGetValue(key, out var snapshot) && snapshot != null && snapshot.count > 0)
            {
                count = snapshot.count;
                spaceMultiplier = snapshot.spaceMultiplier == 0f ? 1f : snapshot.spaceMultiplier;
                return true;
            }

            return false;
        }

        public static void RememberOriginal(string key, int count, float spaceMultiplier)
        {
            if (string.IsNullOrEmpty(key) || count <= 0 || KnownOriginals.ContainsKey(key))
            {
                return;
            }

            KnownOriginals[key] = new OriginalSnapshot
            {
                count = count,
                spaceMultiplier = spaceMultiplier == 0f ? 1f : spaceMultiplier,
            };
            SaveOriginals();
            Mod.log.Info($"Recorded original for '{key}': {count} households.");
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(DataFolder);
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(Entries, Formatting.Indented));
                LastSeenWriteTicks = File.GetLastWriteTimeUtc(FilePath).Ticks;
            }
            catch (Exception e)
            {
                Mod.log.Error($"Failed to save overrides to {FilePath}: {e}");
            }
        }

        public static void SetOverride(string prefabName, int level, int households, int currentCount, float currentSpaceMultiplier)
        {
            if (households <= 0 || string.IsNullOrEmpty(prefabName))
            {
                return;
            }

            string key = $"{prefabName}#{level}";
            if (!Entries.TryGetValue(key, out var entry))
            {
                entry = new HouseholdOverrideEntry
                {
                    prefabName = prefabName,
                    level = level,
                };
                Entries[key] = entry;
            }

            if (!entry.captured)
            {
                if (TryGetKnownOriginal(key, out var knownCount, out var knownSpace))
                {
                    entry.originalCount = knownCount;
                    entry.originalSpaceMultiplier = knownSpace;
                    Mod.log.Info($"Override '{key}': using recorded original of {knownCount} households.");
                }
                else
                {
                    entry.originalCount = currentCount;
                    entry.originalSpaceMultiplier = currentSpaceMultiplier == 0f ? 1f : currentSpaceMultiplier;
                    RememberOriginal(key, currentCount, currentSpaceMultiplier);
                }

                entry.captured = true;
            }

            entry.households = households;
            Save();
            Version++;
        }

        public static PendingRestore RemoveOverride(string prefabName, int level)
        {
            string key = $"{prefabName}#{level}";
            if (!Entries.TryGetValue(key, out var entry))
            {
                return null;
            }

            PendingRestore restore = null;
            if (entry.captured)
            {
                restore = new PendingRestore(entry);
                PendingRestores.Add(restore);
            }

            Entries.Remove(key);
            Save();
            Version++;

            return restore;
        }

        public static void ClearAll()
        {
            foreach (var pair in Entries)
            {
                if (pair.Value.captured)
                {
                    PendingRestores.Add(new PendingRestore(pair.Value));
                }
            }

            Entries.Clear();
            Save();
            Version++;
            Mod.log.Info("All overrides cleared.");
        }
    }
}
