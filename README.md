# FirstGameDemo

Unity 版本：2022.3.61f1c1

## 直接游玩

如果只想运行已打包版本，不需要克隆工程。到 GitHub Release 页面下载 `FirstDemo.zip`，解压后运行其中的游戏可执行文件即可。

## 克隆后打开项目

本仓库保持轻量，不使用 Git LFS。`Assets/Res` 大资源不在 Git 仓库内，需要从 GitHub Release 手动下载资源包后补齐。

1. 克隆仓库。
2. 在 GitHub Release 页面下载所有 `Assets-Res-*.zip` 分包。
3. 将所有分包解压到项目根目录，确保最终路径形如 `Assets/Res/...`。
4. 使用 Unity 2022.3.61f1c1 打开项目。

## 仓库内容

- `Assets/Framework`、`Assets/Game`、`Assets/Data`：项目代码与配置。
- `Assets/Scenes`：工程场景文件。
- `Assets/AddressableAssetsData`：Addressables 配置。
- `ProjectSettings`、`Packages`：Unity 工程设置与包锁定文件。
- `Assets/Res`：通过 Release 附件补齐，不直接进入 Git。
