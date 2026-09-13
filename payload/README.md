# 用户本地材料包

本目录由 scripts/Prepare-Payload.ps1 从 input/desktop-materials 生成，包含用户提供的18个DLL、addon及本地ReShade 6.8.0 Full Add-on Setup；不在线更换版本。

manifest.json 的Vendor target是同名搜索键，运行时仅搜索用户所选GameRoot内已存在的同名DLL，无同名则跳过；不将平铺材料当成游戏目录树。真实逐文件绝对路径在部署前计划中列出并确认。

source-manifest.json 记录实际材料版本、PE架构、长度、SHA-256、复制核对和本机签名结果。哈希不是官方来源证明。18个DLL签名本机验证为Valid；addon未签名；Setup存在ReShade签名，但本机证书链为UnknownError（不受信任根）。Setup为x86安装器，不代表所安装的运行时架构；运行时需另行核验x64。

payload-map.json 记录每个材料的映射规则。当前未读取或写入真实游戏；实际路径尚待用户选择与确认。dxgi为待验证代理候选，游戏内生效尚未验证。
