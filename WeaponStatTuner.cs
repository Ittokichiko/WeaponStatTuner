using System;
using System.Collections.Generic;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("WeaponStatTuner", "Ittokichiko", "1.3.0")]
    [Description("Override weapon stats via config or commands.")]
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
            cmd.AddChatCommand("tune", this, nameof(CmdTune));
        }

        private void OnActiveItemChanged(BasePlayer player, Item oldItem, Item newItem)
        {
            if (newItem == null) return;
            timer.Once(0f, () => ApplyWeaponTuning(newItem));
        }

        private void OnItemAddedToContainer(ItemContainer container, Item item)
        {
            if (container?.parent == null || item == null) return;
            timer.Once(0f, () => ApplyWeaponTuning(item));
        }
        #endregion

        #region Logic
        private void ApplyWeaponTuning(Item item)
        {
            if (item == null) return;
            string shortname = item.info?.shortname ?? "";
            if (!_config.Weapons.TryGetValue(shortname, out var tuning)) return;

            var bp = item.GetHeldEntity() as BaseProjectile;
            if (bp == null) return;

            // DAMAGE
            if (tuning.Damage.HasValue)
                bp.damageScale = tuning.Damage.Value; // Set damage directly on BaseProjectile

            // FIRE RATE
            if (tuning.FireRate.HasValue)
                bp.repeatDelay = tuning.FireRate.Value;

            // PROJECTILE SPEED
            if (tuning.ProjectileSpeed.HasValue)
                bp.projectileVelocityScale = tuning.ProjectileSpeed.Value; // Directly set speed scale

            // MAGAZINE SIZE
            if (tuning.MagazineSize.HasValue && bp.primaryMagazine != null)
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
        private void CmdTune(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, permUse))
            {
                SendReply(player, "You don’t have permission to use this.");
                return;
            }

            if (args.Length == 0)
            {
                SendReply(player, "Usage: /tune <weapon> <stat> <value>\n/tune list <weapon>\n/tune reset <weapon>\n/tune resetall");
                return;
            }

            var sub = args[0].ToLower();

            // LIST
            if (sub == "list")
            {
                if (args.Length < 2)
                {
                    SendReply(player, "Usage: /tune list <weapon_shortname>");
                    return;
                }

                string weapon = args[1];
                if (!_config.Weapons.TryGetValue(weapon, out var tuning))
                {
                    SendReply(player, $"No overrides set for {weapon}");
                    return;
                }

                SendReply(player, $"[WeaponStatTuner] Stats for {weapon}:\n" +
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
                    SendReply(player, "Usage: /tune reset <weapon_shortname>");
                    return;
                }

                string weapon = args[1];
                if (_config.Weapons.Remove(weapon))
                {
                    SaveConfig();
                    SendReply(player, $"[WeaponStatTuner] Reset stats for {weapon} to default.");
                }
                else
                {
                    SendReply(player, $"[WeaponStatTuner] No overrides were set for {weapon}.");
                }
                return;
            }

            // RESETALL
            if (sub == "resetall")
            {
                _config.Weapons.Clear();
                _config.ForcePerfectAccuracy = false;
                SaveConfig();
                SendReply(player, "[WeaponStatTuner] All weapon overrides reset to default.");
                return;
            }

            // SET STAT
            if (args.Length < 3)
            {
                SendReply(player, "Usage: /tune <weapon_shortname> <stat> <value>");
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
                    SendReply(player, "Unknown stat. Use: damage, firerate, speed, magsize, spread, perfect");
                    return;
            }

            SaveConfig();
            SendReply(player, $"[WeaponStatTuner] {weaponName} {stat} set to {valStr}");
        }
        #endregion
    }
}
