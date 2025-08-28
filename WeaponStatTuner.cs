using System.Collections.Generic;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Weapon Stat Tuner", "Gleb+ChatGPT", "2.0.0")]
    [Description("Adjusts weapon stats such as damage, fire rate, magazine size, spread, and projectile speed.")]
    public class WeaponStatTuner : RustPlugin
    {
        private WeaponConfig _config;

        #region Config

        public class WeaponTuning
        {
            public float? Damage { get; set; }
            public float? FireRate { get; set; }
            public float? ProjectileSpeed { get; set; }
            public int? MagazineSize { get; set; }
            public float? Spread { get; set; }
        }

        public class WeaponConfig
        {
            public bool ForcePerfectAccuracy { get; set; } = false;
            public Dictionary<string, WeaponTuning> Weapons { get; set; } = new Dictionary<string, WeaponTuning>();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<WeaponConfig>();
            if (_config?.Weapons == null)
                _config = new WeaponConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config);

        protected override void LoadDefaultConfig()
        {
            _config = new WeaponConfig
            {
                Weapons = new Dictionary<string, WeaponTuning>
                {
                    ["rifle.ak"] = new WeaponTuning
                    {
                        Damage = 1.2f,
                        FireRate = 0.12f,
                        ProjectileSpeed = 1.3f,
                        MagazineSize = 40,
                        Spread = 0.5f
                    },
                    ["pistol.python"] = new WeaponTuning
                    {
                        Damage = 1.5f,
                        FireRate = 0.25f,
                        MagazineSize = 10
                    }
                }
            };
        }

        #endregion

        #region Hooks

        private void OnWeaponDeploy(BaseProjectile bp)
        {
            ApplyWeaponTuning(bp?.GetItem());
        }

        private void OnItemCraftFinished(ItemCraftTask task, Item item)
        {
            if (item != null)
                NextTick(() => ApplyWeaponTuning(item));
        }

        private void OnItemAddedToContainer(ItemContainer container, Item item)
        {
            if (item != null && container?.entityOwner is BasePlayer)
                NextTick(() => ApplyWeaponTuning(item));
        }

        #endregion

        #region Core Logic

        private void ApplyWeaponTuning(Item item)
        {
            if (item == null) return;

            if (!_config.Weapons.TryGetValue(item.info?.shortname ?? "", out var tuning))
                return;

            var bp = item.GetHeldEntity() as BaseProjectile;
            if (bp == null) return;

            // --- DAMAGE ---
            if (tuning.Damage.HasValue)
                bp.damageScale = tuning.Damage.Value;

            // --- FIRE RATE ---
            if (tuning.FireRate.HasValue)
                bp.repeatDelay = tuning.FireRate.Value;

            // --- PROJECTILE SPEED ---
            if (tuning.ProjectileSpeed.HasValue)
                bp.projectileVelocityScale = tuning.ProjectileSpeed.Value;

            // --- MAGAZINE SIZE ---
            if (tuning.MagazineSize.HasValue && bp.primaryMagazine != null)
                bp.primaryMagazine.capacity = tuning.MagazineSize.Value;

            // --- ACCURACY / SPREAD ---
            if (_config.ForcePerfectAccuracy)
            {
                bp.aimCone = bp.hipAimCone = bp.aimSway = bp.aimSwaySpeed = 0f;
            }
            else if (tuning.Spread.HasValue)
            {
                bp.aimCone = bp.hipAimCone = tuning.Spread.Value;
            }
        }

        #endregion
    }
}
