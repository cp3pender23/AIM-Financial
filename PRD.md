# Product Requirements Document (PRD)
## Adaptive Intelligence Model (AIM) Dashboard

---

## 1. Executive Summary

### Product Vision
AIM (Adaptive Intelligence Model) is a predictive analytics dashboard designed for law enforcement agencies to identify, prioritize, and investigate suspicious activity patterns. The platform transforms complex datasets into actionable intelligence, enabling users to quickly identify high-risk subjects and optimize resource allocation.

### Core Value Propositions
- **Visibility** into suspicious activity opportunities
- **Prioritization** for operational procedures
- **Optimization** of resources and personnel

---

## 2. Problem Statement

Law enforcement agencies face challenges in:
- Processing large volumes of suspicious activity reports (SARs)
- Identifying patterns and high-risk subjects from raw data
- Prioritizing investigations based on risk levels
- Making data-driven decisions quickly

---

## 3. Target Audience

**Primary Users:** Law enforcement personnel including:
- Investigators
- Analysts
- Supervisors/Management
- Task force members

**User Needs:**
- Quick identification of high-risk subjects
- Filtering by suspicious activity types
- Geographic and temporal analysis
- Secure access to sensitive data

---

## 4. Technical Stack

| Component | Technology |
|-----------|------------|
| ORM/Database | Prisma (SQLite for dev) |
| UI Framework | Bootstrap 5 |
| JavaScript Library | jQuery 3.7 |
| Data Visualization | Chart.js 4.4 |
| Backend | Express.js |
| Templating | EJS |
| Authentication | Session-based (bcrypt) |

---

## 5. Feature Requirements

### 5.1 Authentication & Security

**Priority:** Critical

| Requirement | Description |
|-------------|-------------|
| Secure Login | Username/password authentication with session management |
| Session Timeout | Auto-logout after 30 minutes of inactivity |
| Role-Based Access | Support for different user permission levels (admin, analyst) |
| Audit Logging | Track user access and actions |
| HTTPS | All traffic encrypted (production) |
| Password Hashing | bcrypt for secure password storage |

### 5.2 Dashboard Header Metrics

**Priority:** High

Display key performance indicators at the top of the dashboard:

| Metric | Description |
|--------|-------------|
| Total Mailings | Aggregate count of all records |
| Oldest Mailing | Earliest date in dataset |
| Newest Mailing | Most recent date in dataset |
| Average Weight | Mean weight across all records |
| Unique Addresses | Distinct address count |

### 5.3 Left Navigation - Dynamic Filters

**Priority:** High

Collapsible filter sections based on the data fields, optimized for law enforcement investigative needs:

#### Filter Categories

| Filter Name | Field | Purpose |
|-------------|-------|---------|
| **Rating** | `rating` | Filter by risk rating (1-5) |
| **Suspicious Activity Type** | `activityType` | Identify specific suspicious patterns |
| **Additional Intel** | `additionalIntel` | Filter by intelligence flags |
| **Destination Division** | `destDivision` | Geographic targeting |
| **Destination City** | `destCity` | Local area focus |
| **Origin State** | `origState` | Source location analysis |
| **White List** | `whiteList` | Exclude known safe entities |
| **Black List Mailer** | `blackListMailer` | Known bad actors |
| **Date Range** | `mailDate` | Temporal filtering |

#### Filter Behavior
- Filters update all dashboard components in real-time
- Multiple filters can be applied simultaneously (AND logic)
- Clear all filters option
- Filter state persistence during session
- Show count of matching records per filter option

### 5.4 Rating Breakdown Chart

**Priority:** High

Horizontal bar chart displaying distribution of records by risk rating.

#### Color Scheme

| Rating | Color | Hex Code | Risk Level |
|--------|-------|----------|------------|
| 5 | Red | `#E53935` | Critical |
| 4 | Orange | `#FB8C00` | High |
| 3 | Yellow | `#FDD835` | Medium |
| 2 | Blue | `#1E88E5` | Low |
| 1 | Dark Blue | `#1565C0` | Minimal |

#### Chart Requirements
- Horizontal bar orientation
- Display count values on bars
- Interactive tooltips
- **Click-to-filter functionality** - clicking a bar filters the data table
- Responsive sizing

### 5.5 Metered Postage Provider Chart

**Priority:** High

Donut/ring chart showing distribution by postage provider.

#### Requirements
- Color-coded segments with legend
- Percentage and count display
- Interactive hover states
- **Click-to-filter capability** - clicking a segment filters the data table
- Top providers highlighted

### 5.6 Data Table

**Priority:** High

Sortable, searchable table displaying filtered results.

| Column | Sortable | Description |
|--------|----------|-------------|
| Rating | Yes | Risk rating with color indicator |
| Mailer Name | Yes | Subject/entity name |
| Mail Date | Yes | Date of activity |
| Origin | Yes | Origin city/state |
| Destination | Yes | Destination city/state |
| Activity Type | Yes | Suspicious activity classification |
| Weight | Yes | Package weight |
| Postage Provider | Yes | Metered postage source |

#### Table Features
- Pagination (25, 50, 100 per page)
- Column sorting (ascending/descending)
- Quick search
- Export functionality (CSV)
- Row click for detail modal view
- "Chart Filters Active" indicator when filtered via charts

---

## 6. Data Model

### Prisma Schema

```prisma
model User {
  id           Int       @id @default(autoincrement())
  username     String    @unique
  passwordHash String    @map("password_hash")
  email        String?
  role         String    @default("analyst")
  lastLogin    DateTime? @map("last_login")
  createdAt    DateTime  @default(now()) @map("created_at")
  updatedAt    DateTime  @updatedAt @map("updated_at")
  auditLogs    AuditLog[]
}

model AuditLog {
  id        Int      @id @default(autoincrement())
  userId    Int      @map("user_id")
  action    String
  details   String?
  ipAddress String?  @map("ip_address")
  createdAt DateTime @default(now()) @map("created_at")
  user      User     @relation(fields: [userId], references: [id])
}

model SuspiciousActivity {
  id              Int      @id @default(autoincrement())
  rating          Int      // 1-5 risk rating
  mailerName      String   @map("mailer_name")
  mailDate        DateTime @map("mail_date")
  origCity        String   @map("orig_city")
  origState       String   @map("orig_state")
  destCity        String   @map("dest_city")
  destState       String   @map("dest_state")
  destDivision    String   @map("dest_division")
  weight          Float
  postageProvider String   @map("postage_provider")
  activityType    String   @map("activity_type")
  additionalIntel String?  @map("additional_intel")
  whiteList       Boolean  @default(false) @map("white_list")
  blackListMailer Boolean  @default(false) @map("black_list_mailer")
  createdAt       DateTime @default(now()) @map("created_at")
  updatedAt       DateTime @updatedAt @map("updated_at")
}
```

---

## 7. User Interface Layout

```
┌─────────────────────────────────────────────────────────────────────────┐
│  [Logo] AIM - Adaptive Intelligence Model              [User] [Logout]  │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐           │
│  │ Total   │ │ Oldest  │ │ Newest  │ │ Average │ │ Unique  │           │
│  │ 162,361 │ │8/14/2021│ │2/9/2022 │ │  1.04   │ │ 96,792  │           │
│  └─────────┘ └─────────┘ └─────────┘ └─────────┘ └─────────┘           │
│                                                                         │
├────────────┬────────────────────────────────────────────────────────────┤
│            │                                                            │
│  FILTERS   │   Rating Breakdown          Metered Postage Provider       │
│            │   (click to filter)         (click to filter)              │
│  ▼ Rating  │   ┌──────────────────┐      ┌──────────────────┐          │
│    □ 5     │   │ ████████████ 5  │      │    ╭───────╮     │          │
│    □ 4     │   │ ██████████   4  │      │   ╱    ●    ╲    │          │
│    □ 3     │   │ ████████     3  │      │  │           │   │          │
│    □ 2     │   │ ██████       2  │      │   ╲         ╱    │          │
│    □ 1     │   │ ████         1  │      │    ╰───────╯     │          │
│            │   └──────────────────┘      └──────────────────┘          │
│  ▼ Activity│                                                            │
│    Type    │   ─────────────────────────────────────────────           │
│            │                                                            │
│  ▼ Dest    │   Suspicious Activity Records  [Chart Filters Active]     │
│    Division│   ┌────────────────────────────────────────────┐          │
│            │   │ Rating │ Name │ Date │ Origin │ Dest │... │          │
│  ▼ Origin  │   ├────────────────────────────────────────────┤          │
│    State   │   │   5    │ ...  │ ...  │  ...   │ ...  │    │          │
│            │   │   4    │ ...  │ ...  │  ...   │ ...  │    │          │
│  ▼ White   │   │   3    │ ...  │ ...  │  ...   │ ...  │    │          │
│    List    │   └────────────────────────────────────────────┘          │
│            │                                                            │
│  ▼ Black   │   [< 1 2 3 ... 10 >]                                       │
│    List    │                                                            │
│            │                                                            │
│  ▼ Date    │                                                            │
│    Range   │                                                            │
└────────────┴────────────────────────────────────────────────────────────┘
```

---

## 8. API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/auth/login` | User authentication |
| GET | `/auth/logout` | End session |
| GET | `/api/metrics` | Header KPIs |
| GET | `/api/ratings` | Rating breakdown data |
| GET | `/api/providers` | Postage provider distribution |
| GET | `/api/activities` | Filtered activity list |
| GET | `/api/activities/:id` | Single activity detail |
| GET | `/api/filters` | Dynamic filter options |

### Query Parameters (for filtering)

| Parameter | Type | Description |
|-----------|------|-------------|
| `rating` | Array | Filter by rating(s) |
| `activityType` | Array | Filter by activity type(s) |
| `destDivision` | Array | Filter by division(s) |
| `destCity` | Array | Filter by city(s) |
| `origState` | Array | Filter by state(s) |
| `postageProvider` | Array | Filter by provider(s) |
| `whiteList` | Boolean | Filter by whitelist status |
| `blackList` | Boolean | Filter by blacklist status |
| `dateFrom` | Date | Start date filter |
| `dateTo` | Date | End date filter |
| `search` | String | Text search |
| `sortBy` | String | Sort column |
| `sortOrder` | String | asc/desc |
| `page` | Number | Page number |
| `limit` | Number | Records per page |

---

## 9. Non-Functional Requirements

### Performance
- Dashboard load time < 3 seconds
- Filter response time < 500ms
- Support for 100+ concurrent users

### Security
- HTTPS required (production)
- Password hashing (bcrypt)
- SQL injection prevention (Prisma)
- XSS protection (Helmet.js)
- CSRF tokens
- Session expiration (30 min idle)

### Accessibility
- WCAG 2.1 AA compliance
- Keyboard navigation
- Screen reader support

### Browser Support
- Chrome (latest 2 versions)
- Firefox (latest 2 versions)
- Edge (latest 2 versions)
- Safari (latest 2 versions)

---

## 10. Success Metrics

| Metric | Target |
|--------|--------|
| User adoption rate | 80% of target users within 30 days |
| Average session duration | 15+ minutes |
| Filter usage rate | 70% of sessions use 2+ filters |
| Investigation initiation rate | 25% increase in cases opened from data |
| User satisfaction score | 4.0+ / 5.0 |

---

## 11. Future Considerations

- **Guardian Integration:** Export intelligence to Guardian Notices
- **Map Visualization:** Geographic heat maps
- **Predictive Scoring:** ML-enhanced risk ratings
- **Custom Alerts:** Threshold-based notifications
- **Report Generation:** Automated intelligence reports
- **Multi-agency Sharing:** Secure collaboration features
- **Advanced Analytics:** Trend analysis and pattern detection

---

## 12. Getting Started

### Installation

```bash
cd /opt/homebrew/var/www/AIM
npm install
npx prisma generate
npx prisma db push
npm run db:seed
```

### Running the Application

```bash
npm start
```

Access at: `http://localhost:3000`

### Default Credentials

| Role | Username | Password |
|------|----------|----------|
| Admin | `admin` | `admin123` |
| Analyst | `analyst` | `analyst123` |

---

## 13. File Structure

```
/opt/homebrew/var/www/AIM/
├── .env                    # Environment configuration
├── .env.example            # Example environment file
├── .gitignore
├── package.json
├── server.js               # Express application entry point
├── PRD.md                  # This document
├── config/                 # Configuration files
├── middleware/
│   └── auth.js             # Authentication middleware
├── prisma/
│   ├── schema.prisma       # Database schema
│   ├── seed.js             # Database seeding script
│   └── dev.db              # SQLite database (development)
├── public/
│   ├── css/
│   │   ├── login.css       # Login page styles
│   │   └── dashboard.css   # Dashboard styles
│   └── js/
│       └── dashboard.js    # Dashboard client-side logic
├── routes/
│   ├── auth.js             # Authentication routes
│   ├── dashboard.js        # Dashboard routes
│   └── api.js              # API endpoints
└── views/
    ├── login.ejs           # Login page template
    ├── dashboard.ejs       # Dashboard template
    └── error.ejs           # Error page template
```

---

**Document Version:** 1.0
**Last Updated:** December 18, 2024
**Status:** Implementation Complete
