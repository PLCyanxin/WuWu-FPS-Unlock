  {
    int last_fixed=4;
    const int force=static_cast<int>(mfgunlock::framecount::g_force_multiplier.load());
    if(!reshade::get_config_value(nullptr,kConfigSection,"WuWaLastFixedMultiplier",last_fixed))
      last_fixed=force>=2&&force<=6?force:4;
    wuwa_last_fixed=last_fixed>=2&&last_fixed<=6?last_fixed:4;
    int mode=-1;
    if(!reshade::get_config_value(nullptr,kConfigSection,"WuWaFrameGenerationModeV1",mode)||mode<0||mode>2)
      mode=!g_enabled.load()?0:(mfgunlock::framecount::g_dynamic_mfg_enabled.load()?2:(force>=2&&force<=6?1:0));
    wuwa_frame_mode=mode;
    mfgunlock::framecount::g_force_multiplier.store(mode==1?wuwa_last_fixed:0);
    mfgunlock::framecount::g_dynamic_mfg_enabled.store(mode==2);
    mfgunlock::framecount::g_fixed_override_status.store(static_cast<unsigned>(
      mode==1?mfgunlock::forcepolicy::FixedOverrideStatus::kPending:mfgunlock::forcepolicy::FixedOverrideStatus::kNative));
    reshade::set_config_value(nullptr,kConfigSection,"WuWaLastFixedMultiplier",wuwa_last_fixed);
    reshade::set_config_value(nullptr,kConfigSection,"WuWaFrameGenerationModeV1",mode);
  }
