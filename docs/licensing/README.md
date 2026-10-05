---
AIGC:
    Label: "1"
    ContentProducer: 001191440300708461136T1XGW3
    ProduceID: 871839cd4a3ed3257c2e115376c7f809_f2692996c0b711f18019525400248c00
    ReservedCode1: P474HbMILBmrHfxS952qBgJTeqscSBJ1Qb8u9Rqkbw/doH88wVlHfemnZ+/QsrVHGXBltqirFQ4eYsePWkkPVdK1W2Us3ud+9O2P0OiGWAm+Fuu5/dUFS1E4RWWtWLJpr/3yiWdbZiKXJOojnMOa7nNhsH/n1CIUu0JXXhoz6SlFgnM5FhzmcLqSrlM=
    ContentPropagator: 001191440300708461136T1XGW3
    PropagateID: 871839cd4a3ed3257c2e115376c7f809_f2692996c0b711f18019525400248c00
    ReservedCode2: P474HbMILBmrHfxS952qBgJTeqscSBJ1Qb8u9Rqkbw/doH88wVlHfemnZ+/QsrVHGXBltqirFQ4eYsePWkkPVdK1W2Us3ud+9O2P0OiGWAm+Fuu5/dUFS1E4RWWtWLJpr/3yiWdbZiKXJOojnMOa7nNhsH/n1CIUu0JXXhoz6SlFgnM5FhzmcLqSrlM=
---



# 授权说明（WpfDataGrid.Enhancements.Pro）

- **开源版（WpfDataGrid.Enhancements）**：MIT 协议，GitHub Releases + NuGet.org 公开分发。
- **Pro 版（WpfDataGrid.Enhancements.Pro）**：商业授权，私有 Feed / 付费下载；Pro 源码不进公共仓库，功能经 `LicenseManager` 校验后可用。

## 授权类型

| 类型 | 适用对象 | 说明 |
|------|----------|------|
| `Personal` | 个人开发者 | 单人多设备使用，价格最低 |
| `Team` | 小团队（≤10 人） | 团队内部共享使用，含基本邮件支持 |
| `Enterprise` | 企业 / 组织 | 不限人数，含优先技术支持与定制服务 |

> 具体价格与购买渠道（占位）：请联系 `sales@example.com` 或访问 https://example.com/pricing。

## 许可证格式

许可证为**自包含 JSON + RSA-SHA256 签名**文件，扩展名统一为 `wpfdatagrid.enhancements.license`：

```json
{
  "licenseId": "9f1c…（32 位 GUID）",
  "licenseType": "Personal | Team | Enterprise",
  "holderName": "购买者名称",
  "holderEmail": "购买者邮箱",
  "issuedAt": "2026-10-05T00:00:00.000Z",
  "expiresAt": null,
  "featureMask": 15,
  "signature": "Base64(RSA-SHA256 签名)"
}
```

- `expiresAt` 为 `null` 表示**永久授权**；非空表示订阅到期日。
- `featureMask` 为功能掩码（位标志），当前功能位：
  `XlsxExport`=1、`PdfExport`=2、`AdvancedFilter`=4、`AsyncVirtualization`=8（`15` = 全部功能）。
- 签名覆盖除 `signature` 外的全部字段的规范化序列化结果（`BuildCanonicalBytes()`），任何字段被篡改都会导致校验失败。

## 购买后如何激活

1. 将购买邮件中附带的许可证文件（`.license`）下载到本地。
2. 放置到默认路径：
   - 当前目录 `wpfdatagrid.enhancements.license`
   - 或通过代码指定：

     ```csharp
     LicenseManager.LicenseFilePath = @"D:\licenses\my.license";
     LicenseManager.ResetCache(); // 路径变更后清理缓存
     ```

3. 校验：`LicenseManager.ValidateLicense()` 返回 `Valid` 即激活成功；业务入口使用 `LicenseManager.EnsureLicensed(ProFeature.XlsxExport)` 抛出明确异常或放行。

## 试用模式（无许可证）

未检测到许可证文件时自动进入试用模式：

- 试用期：默认 **30 天**（`LicenseManager.TrialDays`，可配置），从首次调用起按自然日计算。
- 行数限制：单次导出最多 **5000 行**（`LicenseManager.TrialMaxRows`，可配置）。
- 试用期内 Pro 功能可用；到期后 `ValidateLicense()` 返回 `TrialExpired`，`EnsureLicensed()` 抛出 `LicenseException(TrialExpired)`。
- 试用状态记录在 `wpfdatagrid.enhancements.trial` 文件（记录首次使用日期）。

## 密钥管理（仅授权方 / 开发者）

- **公钥**：内嵌于 `LicenseManager.EmbeddedPublicKeyXml`（RSA-2048，XML 格式），随库分发，仅用于验证签名。
- **私钥**：仅存在于签发工具本地，位于 `keys/` 目录（`private.pem`），**严禁提交到 Git 仓库**（`keys/.gitignore` 已配置忽略）。
- 签发工具：`WpfDataGrid.Enhancements.LicenseTool`（独立控制台项目，**不随 NuGet 包分发**）。

### 签发工具用法

```bash
# 1. 生成密钥对（并将公钥内嵌进 LicenseManager.cs）
dotnet run --project src/WpfDataGrid.Enhancements.LicenseTool -- keygen --embed

# 2. 命令行签一张永久许可证（个人版、全部功能）
dotnet run --project src/WpfDataGrid.Enhancements.LicenseTool -- sign \
  --key keys/private.pem --name "张三" --email "zhangsan@example.com" \
  --type Personal --out demo.license

# 3. 命令行签一张订阅许可证（团队版、仅 xlsx 导出，2027-12-31 到期）
dotnet run --project src/WpfDataGrid.Enhancements.LicenseTool -- sign \
  --key keys/private.pem --name "XX 公司" --email "team@example.com" \
  --type Team --features XlsxExport --expires 2027-12-31 --out team.license

# 4. 校验
dotnet run --project src/WpfDataGrid.Enhancements.LicenseTool -- verify \
  --key-xml keys/wpfdatagrid.enhancements.public.xml --license team.license

# 5. 无参数运行时进入交互式模式
dotnet run --project src/WpfDataGrid.Enhancements.LicenseTool
```

### 错误码（LicenseErrorCode）

| 错误码 | 触发场景 |
|--------|----------|
| `FileNotFound` | 未找到许可证文件（且未进入试用） |
| `SignatureInvalid` | 签名无效 / 字段被篡改 |
| `InvalidFormat` | 文件格式损坏、无法解析 |
| `Expired` | 许可证已过期 |
| `TrialExpired` | 试用期已结束 |
| `FeatureNotLicensed` | 许可证有效但未包含所需功能 |

## FAQ

**Q1：许可证丢了怎么办？**
（占位）联系 sales@example.com 提供购买邮箱，可补发。

**Q2：换电脑 / 重装系统如何迁移？**
（占位）许可证与机器不绑定，拷贝 `.license` 文件即可；如担心泄露，可联系授权方作废旧码重新签发。

**Q3：试用期结束还能继续用开源版吗？**
可以，开源版（过滤 / 主题 / CSV 导出）完全免费不受影响，仅 Pro 功能（xlsx / PDF 导出、高级过滤、大数据虚拟化）受试用限制。

**Q4：Enterprise 授权能开发票吗？**
（占位）支持开具增值税普通/专用发票，请在购买时填写开票信息。

**Q5：订阅到期后数据会丢失吗？**
不会。到期仅停用 Pro 功能，已有导出文件与项目代码不受影响。

---
*（内容由AI生成，仅供参考）*
*（内容由AI生成，仅供参考）*
