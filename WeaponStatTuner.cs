using System;
using System.Collections.Generic;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("WeaponStatTuner", "Ittokichiko", "2.5.0")]
    [Description("Override weapon stats (damage, fire rate, projectile speed, mag size, spread) via config or commands.")]
    public class WeaponStatTuner : RustPlugin
    {
        private const string permUse = "weaponstattuner.use";
        private PluginConfig _config;

        #region Config
        public class WeaponConfig
        {
            public float? Damage { get; set; }
            public float? FireRate { get; set; }
            public float? ProjectileSpeed { get; set; }
            public int? MagazineSize { get; set; }
            public float? Spread { get; set; }
        }

        public class PluginConfig
        {
            public Dictionary<string, WeaponConfig> Weapons { get; set; } = new Dictionary<string, WeaponConfig>();
            public bool ForcePerfectAccuracy { get; set; } = false;
        }

        protected override void LoadDefaultConfig()
        {
            _config = new PluginConfig();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<PluginConfig>() ?? new PluginConfig();
            }
            catch (Exception e)
            {
                PrintError($"Config error: {e.Message}");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(_config, true);
        #endregion

        #region Hooks
        private void Init()
        {
            permission.RegisterPermission(permUse, this);
            AddCovalenceCommand("tune", "CmdTune");
        }

        private void OnActiveItemChanged(BasePlayer player, Item oldItem, Item newItem)
        {
            if (newItem == null) return;
            timer.Once(0f, () => ApplyWeaponTuning(newItem));
        }

        private void OnItemAddedToContainer(ItemContainer container, Item item)
        {
            if (container?.parent == null || item == null) return;
            timer.Once(0f, () => ApplyWeaponTuning(container.parent));
        }

        private void OnItemRemovedFromContainer(ItemContainer container, Item item)
        {
            if (container?.parent == null || item == null) return;
            timer.Once(0f, () => ApplyWeaponTuning(container.parent));
        }
        #endregion

        #region Logic
        private bool TryGetBaseProjectile(Item item, out BaseProjectile bp)
        {
            bp = null;
            if (item == null) return false;
            var ent = item.GetHeldEntity() as BaseProjectile;
            if (ent == null || ent.IsDestroyed) return false;
            bp = ent;
            return true;
        }

        private void ApplyWeaponTuning(Item item)
        {
            if (!TryGetBaseProjectile(item, out var bp)) return;
            if (bp.primaryMagazine == null || bp.primaryMagazine.ammoType == null) return;

            string shortname = item.info?.shortname ?? "";
            if (string.IsNullOrEmpty(shortname)) return;

            if (!_config.Weapons.TryGetValue(shortname, out var tuning)) return;

            // DAMAGE
            if (tuning.Damage.HasValue && bp.primaryMagazine.ammoType.damageTypes != null)
            {
                bp.primaryMagazine.ammoType.damageTypes.Set(Rust.DamageType.Bullet, tuning.Damage.Value);
            }

            // FIRE RATE
            if (tuning.FireRate.HasValue)
                bp.repeatDelay = tuning.FireRate.Value;

            // PROJECTILE SPEED
            if (tuning.ProjectileSpeed.HasValue && bp.primaryMagazine.ammoType.projectileVelocity > 0f)
                bp.projectileVelocityScale = tuning.ProjectileSpeed.Value / bp.primaryMagazine.ammoType.projectileVelocity;

            // MAGAZINE SIZE
            if (tuning.MagazineSize.HasValue)
                bp.primaryMagazine.capacity = tuning.MagazineSize.Value;

            // SPREAD / PERFECT ACCURACY
            if (_config.ForcePerfectAccuracy)
            {
                bp.aimCone = 0f;
                bp.hipAimCone = 0f;
                bp.aimSway = 0f;
                bp.aimSwaySpeed = 0f;
            }
            else if (tuning.Spread.HasValue)
            {
                bp.aimCone = tuning.Spread.Value;
                bp.hipAimCone = tuning.Spread.Value;
            }
        }
        #endregion

        #region Commands
        private void CmdTune(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(permUse))
            {
                player.Reply("You don’t have permission to use this.");
                return;
            }

            if (args.Length == 0)
            {
                player.Reply("Usage: /tune <weapon> <stat> <value>\n/tune list <weapon>\n/tune reset <weapon>\n/tune resetall");
                return;
            }

            var sub = args[0].ToLower();

            // LIST
            if (sub == "list")
            {
                if (args.Length < 2)
                {
                    player.Reply("Usage: /tune list <weapon_shortname>");
                    return;
                }

                string weapon = args[1];
                if (!_config.Weapons.TryGetValue(weapon, out var tuning))
                {
                    player.Reply($"No overrides set for {weapon}");
                    return;
                }

                player.Reply($"[WeaponStatTuner] Stats for {weapon}:\n" +
                    $"- Damage: {tuning.Damage?.ToString() ?? "default"}\n" +
                    $"- FireRate: {tuning.FireRate?.ToString() ?? "default"}\n" +
                    $"- ProjectileSpeed: {tuning.ProjectileSpeed?.ToString() ?? "default"}\n" +
                    $"- MagazineSize: {tuning.MagazineSize?.ToString() ?? "default"}\n" +
                    $"- Spread: {tuning.Spread?.ToString() ?? "default"}\n" +
                    $"- Perfect Accuracy: {_config.ForcePerfectAccuracy}");
                return;
            }

            // RESET
            if (sub == "reset")
            {
                if (args.Length < 2)
                {
                    player.Reply("Usage: /tune reset <weapon_shortname>");
                    return;
                }

                string weapon = args[1];
                if (_config.Weapons.Remove(weapon))
                {
                    SaveConfig();
                    player.Reply($"[WeaponStatTuner] Reset stats for {weapon} to default.");
                }
                else
                {
                    player.Reply($"[WeaponStatTuner] No overrides were set for {weapon}.");
                }
                return;
            }

            // RESETALL
            if (sub == "resetall")
            {
                _config.Weapons.Clear();
                _config.ForcePerfectAccuracy = false;
                SaveConfig();
                player.Reply("[WeaponStatTuner] All weapon overrides reset to default.");
                return;
            }

            // SET STAT
            if (args.Length < 3)
            {
                player.Reply("Usage: /tune <weapon_shortname> <stat> <value>");
                return;
            }

            string weaponName = args[0];
            string stat = args[1].ToLower();
            string valStr = args[2];

            if (!_config.Weapons.ContainsKey(weaponName))
                _config.Weapons[weaponName] = new WeaponConfig();

            var tuningSet = _config.Weapons[weaponName];

            switch (stat)
            {
                case "damage":
                    if (float.TryParse(valStr, out var dmg)) tuningSet.Damage = dmg;
                    break;
                case "firerate":
                    if (float.TryParse(valStr, out var fr)) tuningSet.FireRate = fr;
                    break;
                case "speed":
                    if (float.TryParse(valStr, out var spd)) tuningSet.ProjectileSpeed = spd;
                    break;
                case "magsize":
                    if (int.TryParse(valStr, out var mag)) tuningSet.MagazineSize = mag;
                    break;
                case "spread":
                    if (float.TryParse(valStr, out var spr)) tuningSet.Spread = spr;
                    break;
                case "perfect":
                    if (bool.TryParse(valStr, out var perfect)) _config.ForcePerfectAccuracy = perfect;
                    break;
                default:
                    player.Reply("Unknown stat. Use: damage, firerate, speed, magsize, spread, perfect");
                    return;
            }

            SaveConfig();
            player.Reply($"[WeaponStatTuner] {weaponName} {stat} set to {valStr}");
        }
        #endregion
    }
}
