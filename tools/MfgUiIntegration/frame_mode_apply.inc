// Applied only by an explicit choice, including reselecting the current mode.
void ApplyWuWaFrameMode(int mode) {
  if(mode<0||mode>2)return;
      const int current=static_cast<int>(mfgunlock::framecount::g_force_multiplier.load());
      if(current>=2&&current<=6)wuwa_last_fixed=current;
      const int next_force=mode==1?wuwa_last_fixed:0;
      mfgunlock::framecount::g_force_multiplier.store(next_force);
      mfgunlock::framecount::g_dynamic_mfg_enabled.store(mode==2);
      mfgunlock::framecount::NotifyFixedMultiplierChanged(next_force);
      mfgunlock::framecount::NotifyDynamicModeChanged();
      wuwa_frame_mode=mode;
      reshade::set_config_value(nullptr,kConfigSection,"WuWaLastFixedMultiplier",wuwa_last_fixed);
      reshade::set_config_value(nullptr,kConfigSection,"WuWaFrameGenerationModeV1",mode);
      reshade::set_config_value(nullptr,kConfigSection,"ForceMultiplier",next_force);
      reshade::set_config_value(nullptr,kConfigSection,"DynamicMFG",mode==2?1:0);
      // An old installation may have disabled the whole addon until restart.
      // Preserve its restart-required notice rather than claiming patches exist.
      g_configured_enabled.store(true);
      reshade::set_config_value(nullptr,kConfigSection,"Enabled",1);
 }

