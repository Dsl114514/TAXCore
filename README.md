# TAXCore
**一个使用AI辅助开发的Hacknet Mod**

---

##  使用要求

- 安装 [**Hacknet-Pathfinder**](https://github.com/Arkhist/Hacknet-Pathfinder)
- **在扩展目录创建 `TAXCore_Config.json` 文件**

```json
{
  "JavaPath": "D:\\java8\\bin\\java.exe",      // Java路径
  "QemuPath": "D:\\Program Files\\qemu",       // QEMU路径
  "FFmpegPath": "FFmpeg\\bin",                 // FFmpeg路径
  "WallpaperFPS": 60,                          // 动态壁纸FPS
  "UseStaticWallpaper": false,                 // 是否使用静态壁纸
  "UseVRAMCache": true,                        // 启用渲染缓存
  "VideoScale": 1.0                            // 视频压缩倍率
}
```

---

##  动态壁纸功能

本Mod允许使用视频作为Hacknet壁纸！

### 基础用法
在Hacknet主题文件中添加 `videoBg` 属性即可播放视频：

```xml
<CustomTheme videoBg="Videos/my_video.mp4">
```

### 高级优化：渲染缓存
对于配置较低的设备，推荐使用渲染缓存功能：

1. 在游戏中使用 `renderCache` 命令生成渲染缓存
2. 将 `videoBg` 替换为 `renderCache` 并指定 `.txc` 文件路径

```xml
<CustomTheme renderCache="TXC/my_TXC.txc">
```

---

##  可执行文件支持

### Python 执行器
- **标签**: `#PYTHON#`
- **功能**: 允许执行Python脚本

### Java 执行器
- **标签**: `#JAVA#`
- **功能**: 支持 `-jar` 参数，使用 `java -jar my_java.jar` 运行
- **注意**: Hacknet也能玩Minecraft！

### Vim 文本编辑器
- **标签**: `#VIM#`
- **功能**: 内置简易文本编辑器
- **使用方法**: `vim 文件名.txt`（支持其他格式）
- ⚠️ **警告**: 不要修改可执行文件，否则可能无法运行

### QEMU 虚拟机
- **标签**: `#QEMU#`
- **功能**: 调用系统DLL，将QEMU窗口嵌入游戏，支持加载ISO文件

### 视频播放器
- **标签**: `#VIDEOU#`
- **功能**: 允许播放视频
- ⚠️ **注意**: 声音与画面可能存在不同步问题

---

##  文件映射系统

> **格式**: `<file path="path" name="name">#XXX#:真实路径</file>`

| 映射类型 | 标签 | 用途 |
|---------|------|------|
| Python脚本 | `#PYF#:` | 映射.py文件 |
| Jar文件 | `#JAVAF#:` | 映射.jar文件 |
| 视频文件 | `#VIDEOF#:` | 映射视频文件 |
| ISO镜像 | `#QU_ISO#:` | 映射.iso文件 |

---

##  Actions（动作指令）

### 节点地图工具
- **全屏网络图**: `<FullscreenNetmap/>`
- **恢复界面**: `<RestoreUI/>`
- **用途**: 开启/恢复超绝节点列阵工具，适合扩展制作时显示节点列阵

### 序列帧播放
- **开始播放**: `<PlayImageSequence/>`
- **参数**:
  - `Folder`: 文件路径
 ### IRC&HUB滚动条
- **加入滚动条方便翻看信息**-
