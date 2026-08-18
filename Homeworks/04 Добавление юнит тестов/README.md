# PromoCodeFactory: gRPC и SignalR API для Customers

Помимо существующего REST API сервис предоставляет два альтернативных транспорта.

## gRPC

Контракт находится в `src/PromoCodeFactory.WebHost/Protos/customers.proto`. Сервис
`customers.CustomersApi` поддерживает методы `GetAll`, `GetById`, `Create`, `Update`
и `Delete`. Для ручной проверки после запуска приложения можно использовать
`grpcurl` (порт возьмите из сообщения `Now listening on`):

```powershell
grpcurl -plaintext localhost:5000 list
grpcurl -plaintext -d '{}' localhost:5000 customers.CustomersApi/GetAll
```

Если приложение слушает HTTPS, используйте `grpcurl -insecure` и HTTPS-порт.

## SignalR

Hub доступен по адресу `/hubs/customers` и предоставляет методы `GetAll`,
`GetById`, `Create`, `Update`, `Delete`. Клиенты также могут подписаться на
`CustomerCreated`, `CustomerUpdated` и `CustomerDeleted`.

Минимальный пример JavaScript-клиента:

```javascript
const connection = new signalR.HubConnectionBuilder()
  .withUrl("https://localhost:5001/hubs/customers")
  .build();

connection.on("CustomerCreated", customer => console.log(customer));
await connection.start();
const customers = await connection.invoke("GetAll");
```

Оба API используют общий `CustomerService`, поэтому правила поиска предпочтений,
валидации и обработки отсутствующих клиентов одинаковы.
