# Financial System — Variant B (v1 Cash Logic)

Backend: **ASP.NET Core (.NET 8)** + **PostgreSQL**  
Запуск: Docker Compose.

## Быстрый старт
```bash
docker compose up --build
```

Swagger: http://localhost:8080/swagger

## Логин (админ)
- email: `admin@local`
- password: `Admin123!`

## Cash v1 (что добавлено)
- Открыть смену
- Закрыть смену (с расчетным остатком)
- Создать кассовую операцию (Draft)
- Провести кассовую операцию (Posted) → **создает LedgerEntry**
- Проверка: запрет минус остатка (настройка `Rules__DisallowNegativeCash=true`)
- Остаток по кассе (на дату/сейчас) по данным ledger

### Основные эндпоинты
- POST `/auth/login`
- GET `/me`
- POST `/cash/shifts/open`
- POST `/cash/shifts/{id}/close`
- POST `/cash/operations`
- POST `/cash/operations/{id}/post`
- GET `/cash/balance?cashBoxId=...`

> Если ты раньше уже запускал старую версию и хочешь обновить схему/данные с нуля:
```bash
docker compose down -v
docker compose up --build
```
(флаг `-v` удалит volume базы)
