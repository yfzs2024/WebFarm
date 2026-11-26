# WebFarm HTTP API 接口规范

本文档描述 WebFarm 服务端对外提供的 HTTP 接口，包括请求/响应格式、鉴权方式与错误语义。

## 1. 基本约定

| 项目 | 约定 |
| --- | --- |
| Base URL | `http://localhost:5038`（开发环境，见 `Properties/launchSettings.json`） |
| 协议 | HTTP/1.1，JSON over HTTPS（生产环境建议只开 HTTPS） |
| 请求体 | `Content-Type: application/json`，字段名使用 **camelCase** |
| 响应体 | `application/json`，字段名使用 **camelCase** |
| 时间格式 | ISO 8601 UTC，例如 `2026-11-30T08:00:00.0000000Z` |
| 字符编码 | UTF-8 |

所有接口均为 `POST`，且路径区分大小写、**不带**尾部斜杠。

## 2. 鉴权

除白名单外的所有接口都需要在请求头中携带 token：

```
Authorization: Bearer <token>
```

服务端也兼容直接写 token 值的形式（`Authorization: <token>`），但推荐统一使用 `Bearer` 前缀。

- **白名单（无需鉴权）**：`/api/register`、`/api/login`
- token 由注册或登录接口签发，有效期 **7 天**
- `User` 与 `UserToken` 是一对一关系，**同一用户只有一个有效 token**：
  - 再次登录会**轮换** token（换新值并重新计时 7 天），旧 token 立即失效
  - 修改密码**不会**作废当前 token，当前会话可继续使用
- 注销（删除用户）后 token 随用户级联删除，立刻失效
- 登出（`/api/logout`）只删除 token，不删除用户数据，之后需重新登录获取新 token

鉴权失败统一返回 `401`，响应体为：

```json
{ "message": "缺少Token" }
```

`message` 可能为 `缺少Token` 或 `Token无效或已过期`（token 不存在、已过期、已被轮换或用户已注销）。

> 说明：端点内部校验（密码错误等）返回的 `401` 没有响应体，只有状态码。

## 3. 接口列表

| 方法 | 路径 | 鉴权 | 说明 |
| --- | --- | --- | --- |
| POST | `/api/register` | 否 | 注册并签发 token |
| POST | `/api/login` | 否 | 登录，校验密码并（重新）签发 token |
| POST | `/api/cancel` | 是 | 注销账号（删除用户及其数据） |
| POST | `/api/changepassword` | 是 | 修改当前登录用户的密码 |
| POST | `/api/logout` | 是 | 登出，作废当前 token（不删除用户） |

---

### 3.1 注册 `POST /api/register`

无需鉴权。创建用户并直接返回 token。

**请求体**

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `userName` | string | 是 | 用户名，全局唯一 |
| `password` | string | 是 | 明文密码，服务端用 PBKDF2 哈希后存储 |

```json
{ "userName": "farmer01", "password": "pw123456" }
```

**响应**

- `200 OK`

```json
{ "token": "8b7390004bfe4db08c95969bbb8d44ec", "expiresAt": "2026-12-02T08:14:24.7220128Z" }
```

- `409 Conflict` —— 用户名已存在，响应体是 JSON 字符串：

```json
"用户名已存在"
```

**示例**

```bash
curl -X POST http://localhost:5038/api/register \
  -H 'Content-Type: application/json' \
  -d '{"userName":"farmer01","password":"pw123456"}'
```

---

### 3.2 登录 `POST /api/login`

无需鉴权。校验用户名与密码；成功后签发新 token（若已有 token 则替换并续期）。

**请求体**

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `userName` | string | 是 | 用户名 |
| `password` | string | 是 | 明文密码 |

```json
{ "userName": "farmer01", "password": "pw123456" }
```

**响应**

- `200 OK` —— 与注册相同的 token 结构：

```json
{ "token": "2222d941e3e94272891688b5dfcf6e7e", "expiresAt": "2026-12-02T08:14:24.7220128Z" }
```

- `401 Unauthorized` —— 用户不存在或密码错误（不区分，避免暴露用户名是否存在），无响应体。

**示例**

```bash
curl -X POST http://localhost:5038/api/login \
  -H 'Content-Type: application/json' \
  -d '{"userName":"farmer01","password":"pw123456"}'
```

---

### 3.3 注销 `POST /api/cancel`

需要鉴权。**只校验密码**，用户由 token 确定，因此无需传用户名，也无法注销他人账号。

删除用户时会级联删除该用户的 token 与农田数据，操作不可撤销。

**请求体**

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `password` | string | 是 | 当前账号密码，用于二次确认 |

```json
{ "password": "pw123456" }
```

**响应**

- `200 OK` —— 空响应体
- `401 Unauthorized` —— 未携带/携带无效 token，或密码错误

**示例**

```bash
curl -X POST http://localhost:5038/api/cancel \
  -H 'Authorization: Bearer <token>' \
  -H 'Content-Type: application/json' \
  -d '{"password":"pw123456"}'
```

---

### 3.4 修改密码 `POST /api/changepassword`

需要鉴权。校验旧密码后写入新密码的哈希；用户由 token 确定，无需传用户名。

**请求体**

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `oldPassword` | string | 是 | 原密码 |
| `newPassword` | string | 是 | 新密码 |

```json
{ "oldPassword": "pw123456", "newPassword": "newpw789" }
```

**响应**

- `200 OK` —— 空响应体
- `401 Unauthorized` —— 未携带/携带无效 token，或旧密码错误

**示例**

```bash
curl -X POST http://localhost:5038/api/changepassword \
  -H 'Authorization: Bearer <token>' \
  -H 'Content-Type: application/json' \
  -d '{"oldPassword":"pw123456","newPassword":"newpw789"}'
```

---

### 3.5 登出 `POST /api/logout`

需要鉴权。作废当前 token（从 `UserTokens` 表中删除），**不删除用户**，用户数据与农田都保留。

登出后该 token 立即失效（继续使用会返回 `401`），再次登录即可获得新 token。

**请求体**：无。

**响应**

- `200 OK` —— 空响应体
- `401 Unauthorized` —— 未携带/携带无效 token

**示例**

```bash
curl -X POST http://localhost:5038/api/logout \
  -H 'Authorization: Bearer <token>'
```

---

## 4. 状态码汇总

| 状态码 | 出现场景 | 响应体 |
| --- | --- | --- |
| `200` | 请求成功 | 注册/登录：`TokenResponse`；注销/改密/登出：空 |
| `401` | token 缺失、无效、过期，或密码校验失败 | 中间件拦截：`{ "message": "..." }`；端点校验失败：空 |
| `409` | 注册时用户名已存在 | JSON 字符串 `"用户名已存在"` |

框架层的 `400`（请求体格式错误/反序列化失败）、`404`（路径不存在）、`405`（方法不支持）沿用 ASP.NET Core 默认行为。

## 5. 数据模型

| 类型 | 字段 | 说明 |
| --- | --- | --- |
| `TokenResponse` | `token` string | 32 位十六进制 token（GUID `N` 格式） |
| | `expiresAt` DateTime | 过期时间，UTC |

## 6. 注意事项

1. **密码长度/强度暂未校验**：服务端只做哈希存储，建议客户端限制用户名 ≤ 25 字符、密码强度足够；数据库中 `UserName` 有唯一索引（重复注册返回 `409`）。
2. **token 轮换**：两个客户端同时登录同一账号时，后登录者会使先登录者的 token 失效，需要前端在收到 `401` 时引导重新登录。
3. **重启服务不会使 token 失效**：token 存在数据库 `UserTokens` 表中，过期时间按 UTC 计算。
4. 新增需要登录的接口时：请求处理函数里从 `HttpContext.Items["User"]` 取 `User` 实体即可（常量见 `TokenAuthMiddleware.UserItemKey`），无需重复解析 token；同时记得把无需鉴权的路径加进 `TokenAuthMiddleware._exemptUrls`。
