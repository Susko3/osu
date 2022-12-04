// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Globalization;
using osu.Framework.Bindables;
using osu.Framework.Configuration;
using osu.Framework.Localisation;
using osu.Game.Configuration;

namespace osu.Game.Localisation
{
    public class OsuLocalisationManager : LocalisationManager
    {
        private readonly BindableBool prefer24HourTime = new BindableBool();

        public OsuLocalisationManager(FrameworkConfigManager frameworkConfig, OsuConfigManager config)
            : base(frameworkConfig)
        {
            config.BindWith(OsuSetting.Prefer24HourTime, prefer24HourTime);
            prefer24HourTime.BindValueChanged(_ => RefreshCulture());
        }

        protected override IFormatProvider CustomiseCultureAndFormatProvider(CultureInfo culture)
        {
            return new OsuFormatProvider(culture, prefer24HourTime.Value);
        }
    }
}
