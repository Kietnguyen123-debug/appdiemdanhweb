# Quản lý phân ca — Hybrid Group & Individual Exception

## REST API

### `POST /api/v1/schedules/groups/bulk-assign`

```json
{
  "workGroupId": 12,
  "from": "2026-10-01",
  "to": "2026-10-31",
  "shiftId": 2,
  "days": null
}
```

`days` có thể truyền để áp dụng lịch chu kỳ theo từng ngày: `[ { "workDate": "2026-10-01", "shiftId": 1 } ]`. API upsert theo khóa `(WorkGroupId, WorkDate)` và không tạo record cá nhân.

### `POST /api/v1/schedules/employees/override`

```json
{
  "items": [
	{ "employeeId": 1001, "workDate": "2026-10-12", "overrideShiftId": 3, "reason": "Đổi ca trực hộ" }
  ]
}
```

### `DELETE /api/v1/schedules/employees/override/{id}`

Xóa bản ghi ngoại lệ; ngày đó tự động quay về GroupSchedule hoặc EmployeeShift mặc định.

### `GET /api/v1/schedules/roster?departmentId=5&from=2026-10-01&to=2026-10-31`

Response gồm `employees[].days[]`, mỗi ô có `source`: `Exception`, `Group` hoặc `Default`, và `exceptionId` để hiển thị badge/hoàn tác.

## Resolution waterfall

`EmployeeShiftException` → `GroupMember` hiệu lực + `GroupSchedule` → `EmployeeShift` hiệu lực gần nhất. Không nhân bản lịch nhóm thành lịch cá nhân.

## UI component structure

Razor Pages hiện tại dùng `Pages/Admin/Shifts/Management.cshtml`:

- `RosterGrid`: render header ngày, sticky employee/group column và cell theo source.
- `RosterCell`: inherited cell màu xanh nhạt; exception cell màu cam, viền highlight.
- `CellOverridePopover`: chọn ca, nhập lý do, submit `OnPostOverride`.
- `OverrideBadge`: ký hiệu `!` và `exceptionId`; submit `OnPostRevert` để xóa exception.
- `GroupBulkAssignToolbar`: chọn nhóm/ca/ngày và submit `OnPostGroupAssign`.

Nếu tách frontend React/Vue, mapping tương ứng là:

```text
RosterPage
├── RosterToolbar (month, department, view mode)
├── GroupRosterGrid
│   └── GroupScheduleCell
└── EmployeeRosterGrid
	└── EmployeeRosterCell
		├── OverrideBadge
		└── CellOverridePopover
```
