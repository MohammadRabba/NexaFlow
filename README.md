# 🚀 NexaFlow

## Multi-Tenant Project Management SaaS

NexaFlow is a production-grade **.NET 10 backend** demonstrating modern enterprise application architecture through:

- Clean Architecture
- Modular Monolith
- CQRS
- Domain-Driven Design (DDD)
- Defense-in-Depth Multi-Tenancy
- Domain Events
- Automated Testing
- Production-oriented Infrastructure

> NexaFlow is **not a CRUD tutorial**.
>
> It is a portfolio-quality system designed to demonstrate how a maintainable, secure, and scalable SaaS backend can evolve from a solid architectural foundation toward enterprise-grade infrastructure.

The project is developed through **11 structured phases**, progressing from foundational architecture to authentication, authorization, messaging, caching, observability, comprehensive testing, and CI/CD.

---

## 📊 Project Status

### Current Phase

**Phase 1 — Foundation** ✅

| Area | Status |
|---|---|
| Clean Architecture | ✅ |
| CQRS Foundation | ✅ |
| Domain Model | ✅ |
| Multi-Tenancy Foundation | ✅ |
| Audit Stamping | ✅ |
| Domain Events | ✅ |
| EF Core Infrastructure | ✅ |
| OpenAPI | ✅ |
| Docker Support | ✅ |
| Health Checks | ✅ |
| ADR Documentation | ✅ |
| Automated Tests | ✅ |

**Current test count: 37 passing tests**

---

## ✨ Highlights

### What makes NexaFlow different?

- ✅ Strict Clean Architecture with enforced dependency boundaries
- 🏢 Multi-Tenant SaaS architecture with defense-in-depth isolation
- 🔀 CQRS + MediatR application design
- 🧠 Domain-Driven Design with explicit business rules and invariants
- 🛡️ Fail-closed tenant filtering using EF Core global query filters
- 📋 Auditing and Domain Events built into the architecture
- 🐳 Docker-ready deployment
- 📊 Structured logging and health monitoring
- 🧪 Layered automated testing strategy
- 📨 Roadmap toward messaging and event-driven architecture
- ⚡ Planned Redis caching
- 🔭 Planned OpenTelemetry observability
- 🚀 Planned CI/CD and production deployment

---

## 🎯 Project Goals

NexaFlow is designed to demonstrate how a modern SaaS backend can be engineered with a strong focus on:

- Maintainability
- Separation of concerns
- Secure multi-tenancy
- Explicit business rules
- Scalable application boundaries
- Testability
- Operational readiness
- Architectural consistency

The repository intentionally evolves through structured development phases, making architectural decisions explicit and traceable over time.

---

# 🏗️ Architecture

NexaFlow combines several complementary architectural patterns:

┌─────────────────────────────────────────────┐
│              NexaFlow Backend               │
├─────────────────────────────────────────────┤
│                                             │
│  Clean Architecture                        │
│          +                                  │
│  Modular Monolith                          │
│          +                                  │
│  CQRS                                      │
│          +                                  │
│  Domain-Driven Design                      │
│          +                                  │
│  Event-Driven Evolution                    │
│                                             │
└─────────────────────────────────────────────┘

Test Projects
Project	Purpose
NexaFlow.Domain.Tests	Business rules and domain invariants
NexaFlow.Application.Tests	Application pipeline and dependency validation
NexaFlow.Api.Tests	API endpoint and smoke testing
NexaFlow.IntegrationTests	Integration testing with PostgreSQL

Run Tests
dotnet test NexaFlow.slnx

🐳 Getting Started
Prerequisites
.NET 10 SDK

Docker

Docker Compose

PostgreSQL 16 (if running without Docker)

Run with Docker
git clone <repository-url>

cd NexaFlow

docker compose up -d

Verify the Application
Liveness
curl http://localhost:8080/health/live

Readiness
curl http://localhost:8080/health/ready

OpenAPI
curl http://localhost:8080/openapi/v1.json

📚 Documentation
Document	Description
architecture.md	System architecture
authentication.md	Authentication design
api.md	API documentation
database.md	Database schema and persistence
docs/decisions/	Architectural Decision Records

📜 Architectural Decisions
NexaFlow documents important architectural decisions using ADRs (Architectural Decision Records).

ADR	Decision
ADR-001	PostgreSQL 16
ADR-002	Clean Architecture
ADR-003	Modular Monolith
ADR-004	Multi-Tenant Architecture
ADR-005	RabbitMQ + Outbox (upcoming)
ADR-006	Redis Strategy (upcoming)
ADR-007	Authorization Model (upcoming)

The goal is to make architectural decisions explicit, reviewable, and traceable as the system evolves.

🔮 Future Vision
NexaFlow is designed to evolve from a foundational SaaS backend into a production-oriented platform supporting:

🔐 Authentication & Authorization

📁 Project Management

✅ Task Management

🔔 Real-Time Notifications

📨 Event-Driven Architecture

⚡ Redis Caching

🔭 OpenTelemetry Observability

📋 Comprehensive Audit Trails

🧪 Expanded Integration & E2E Testing

🚀 Automated CI/CD Pipelines

☁️ Production Deployment

🧱 Technology Stack
Technology	Purpose
.NET 10	Backend platform
ASP.NET Core	HTTP API
C#	Primary language
EF Core	ORM / persistence
PostgreSQL 16	Relational database
MediatR	CQRS request pipeline
Serilog	Structured logging
Docker	Containerization
OpenAPI	API documentation
RabbitMQ	Planned messaging infrastructure
Redis	Planned caching infrastructure
OpenTelemetry	Planned observability

📈 Engineering Principles
NexaFlow is built around a few core principles:

Keep business rules close to the domain.

Make invalid states difficult to represent.

Fail closed when security boundaries are uncertain.

Prefer explicit architectural boundaries over implicit conventions.

Make infrastructure replaceable through abstractions.

Test behavior, not implementation details.

Document important architectural decisions.

📄 License
Proprietary Portfolio Project

This project is developed for portfolio and educational demonstration purposes and showcases modern backend engineering practices using the .NET ecosystem and production-oriented architectural patterns.

⭐ About NexaFlow
NexaFlow is an evolving demonstration of how a modern SaaS backend can be designed, tested, secured, documented, and progressively extended without sacrificing architectural integrity.

Built with .NET. Designed with architecture. Engineered for evolution.
