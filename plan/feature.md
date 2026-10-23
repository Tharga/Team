# Ownerless team race (#342)

Goal: the owner rules hold when two member actions overlap, because the store refuses the write when the rule no longer holds.

Scope:
- TeamServiceBase: three virtual guarded writes (role unless owner, remove with optional last-administrator condition, transfer), returning whether they applied. Defaults keep today's behaviour. On refusal the existing guard messages are thrown.
- Mongo: conditional updates (the filter carries the rule); transfer as one update with arrayFilters via the collection's ExecuteAsync.

Acceptance:
- Each overlapping case leaves exactly one owner; a team without an owner keeps an administrator.
- A refused call throws the existing guard's message.
- A store that does not implement the guarded writes behaves as today.
