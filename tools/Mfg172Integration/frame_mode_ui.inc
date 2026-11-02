    int mode=wuwa_frame_mode;
    ImGui::TableNextRow(); ImGui::TableNextColumn();
    ImGui::TextUnformatted("帧生成模式");
    ImGui::TableNextColumn(); ImGui::SetNextItemWidth(-1.0f);
    bool mode_changed=false;
    if(ImGui::BeginCombo("##wuwa_fg_mode",mode==3?"关闭":mode==2?"Dynamic":mode==1?"固定倍率":"游戏默认")) {
      const char* modes[]={"游戏默认","固定倍率","Dynamic","关闭"};
      for(int candidate : {2,1,0,3}) {
        const bool blocked=(candidate==2&&(dynamic_game_blocked||dynamic_hard_unavailable))||(candidate==3&&!CanApplyCompanionFrameOff());
        ImGui::BeginDisabled(blocked);
        if(ImGui::Selectable(modes[candidate],mode==candidate)){mode=candidate;mode_changed=true;}
        ImGui::EndDisabled();
      }
      ImGui::EndCombo();
    }
    if(mode_changed){ApplyWuWaFrameMode(mode);configured_enabled=true;}
    ConfigureCompanionFrameMode(mode,mode_changed);
    if(mode==1) {
      int force=static_cast<int>(mfgunlock::framecount::g_force_multiplier.load());
      ImGui::TableNextRow();ImGui::TableNextColumn();
      ImGui::TextUnformatted("固定帧生成倍率");
      ImGui::TableNextColumn();ImGui::SetNextItemWidth(-1.0f);
      if(DrawFixedDerivedCompanion(&force)) {
        wuwa_last_fixed=force;
        mfgunlock::framecount::g_force_multiplier.store(force);
        mfgunlock::framecount::NotifyFixedMultiplierChanged(force);
        reshade::set_config_value(nullptr,kConfigSection,"ForceMultiplier",force);
        reshade::set_config_value(nullptr,kConfigSection,"WuWaLastFixedMultiplier",force);
      }
    }
    bool dynamic_mfg=mode==2&&!dynamic_game_blocked;
    if(dynamic_mfg) {
      ImGui::BeginDisabled(dynamic_hard_unavailable);
      DrawDynamicDerivedCompanion(runtime);
      ImGui::EndDisabled();
    }

