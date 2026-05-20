# PhotoHelper

PhotoHelper 是一款 Windows WPF 桌面应用，帮助摄影师扫描、导入并归档照片，同时为每个照片库维护独立的 SQLite 数据库。

## 核心特性

- 扫描源文件夹或相机存储卡，发现新照片。
- 按日期结构归档照片。
- 每个照片库独立保存数据库、日志与设置。
- 支持从已有库重建历史数据库。
- 简洁的导入进度与日志面板。

## 数据存储结构

每个目标库会在根目录下保存自己的隐藏数据目录：

```
<TargetRoot>\.photohelper
├─ Data\photohelper.db
├─ Logs\photohelper_YYYYMMDD.log
└─ settings.json
```

全局应用状态（最近使用的库列表）保存位置：

```
%LOCALAPPDATA%\PhotoHelper\global.settings.json
```

## 环境要求

- Windows 10/11
- .NET SDK 8

## 运行（开发）

使用 Visual Studio 或 VS Code 打开解决方案并运行 `PhotoHelper.csproj`。

## 使用流程

1. 点击 **选择源路径** 选择源文件夹（相机卡或中转目录）。
2. 点击 **选择目标路径** 选择照片库根目录。
3. 可通过 **库** 下拉框切换最近库。
4. 点击 **开始智能导入** 执行导入。
5. 使用 **重建历史数据库** 重新扫描已有库。


## 项目结构

- `PhotoHelper/` - WPF 应用
	- `Services/` - 工作流与编排
	- `Data/` - SQLite 持久化
	- `Models/` - 领域模型
	- `Utils/` - 工具与配置
	- `Logging/` - 日志基础设施
