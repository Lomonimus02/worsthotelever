using System;
using System.IO;
using UnityEngine;

namespace WorstHotel
{
    [Serializable] public sealed class HotelUpgradeDefinition
    {
        public string id, name, description;
        public int price;
    }

    public static class HotelUpgradeCatalog
    {
        [Serializable] private sealed class Document
        {
            public int version = 0;
            public HotelUpgradeDefinition[] upgrades = null;
        }
        private static HotelUpgradeDefinition[] definitions;
        private static readonly string[] RequiredIds = { "toolbox", "cart", "linen", "coffee", "bed", "tv", "room105", "room106" };

        // Each caller gets independent entries; UI cannot edit the authoritative catalogue by accident.
        public static HotelUpgradeDefinition[] All
        {
            get
            {
                if (definitions == null)
                {
                    TextAsset asset = Resources.Load<TextAsset>("upgrades-mvp");
                    if (asset == null) throw new InvalidDataException("Не найден каталог Resources/upgrades-mvp.json.");
                    definitions = Parse(asset.text);
                }
                var result = new HotelUpgradeDefinition[definitions.Length];
                for (int i = 0; i < result.Length; i++) result[i] = Copy(definitions[i]);
                return result;
            }
        }
        public static HotelUpgradeDefinition Find(string id)
        {
            foreach (HotelUpgradeDefinition definition in All) if (definition.id == id) return definition;
            return null;
        }
        public static HotelUpgradeDefinition[] Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 100000) throw new InvalidDataException("Пустой или слишком большой каталог улучшений.");
            Document document;
            try { document = JsonUtility.FromJson<Document>(json); }
            catch (ArgumentException error) { throw new InvalidDataException("Неверный JSON каталога улучшений.", error); }
            if (document == null || document.version != 1 || document.upgrades == null || document.upgrades.Length != RequiredIds.Length)
                throw new InvalidDataException("Неверная версия или состав каталога улучшений.");
            foreach (string id in RequiredIds)
            {
                int matches = 0;
                foreach (HotelUpgradeDefinition entry in document.upgrades)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.name) || string.IsNullOrWhiteSpace(entry.description) ||
                        entry.name.Length > 100 || entry.description.Length > 500 || entry.price < 1 || entry.price > 1000000)
                        throw new InvalidDataException("Недопустимое улучшение или цена.");
                    if (entry.id == id) matches++;
                }
                if (matches != 1) throw new InvalidDataException("Отсутствующий или повторный ID улучшения: " + id);
            }
            var result = new HotelUpgradeDefinition[document.upgrades.Length];
            for (int i = 0; i < result.Length; i++) result[i] = Copy(document.upgrades[i]);
            return result;
        }
        public static int FinishPrice(string finish)
        {
            return finish == "original" ? 40 : finish == "warm" || finish == "cool" ? 100 : 0;
        }
        private static HotelUpgradeDefinition Copy(HotelUpgradeDefinition value)
        {
            return new HotelUpgradeDefinition { id = value.id, name = value.name, description = value.description, price = value.price };
        }
    }
}
