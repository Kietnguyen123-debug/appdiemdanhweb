# Salary Configuration Module

## REST API contract

These endpoints are the target contract for the component-based payroll module. They can be implemented behind the existing `Admin,HR` authorization policy and granular salary permissions.

| Method | Endpoint | Purpose |
| --- | --- | --- |
| GET | `/api/payroll/components` | List active salary components, filtered by category/visibility permission. |
| POST | `/api/payroll/components` | Create a component with calculation, tax and insurance flags. |
| PUT | `/api/payroll/components/{id}` | Version/update component metadata; do not rewrite historical details. |
| GET | `/api/payroll/templates` | List salary templates and component defaults. |
| POST | `/api/payroll/templates` | Create a template with component formulas/defaults. |
| GET | `/api/payroll/employees/{employeeId}/salary?at=yyyy-MM-dd` | Return the active snapshot at a point in time. |
| POST | `/api/payroll/employees/{employeeId}/salary-revisions` | Create a `PendingApproval` decision with `EffectiveDate`, details, reason and decision number. |
| POST | `/api/payroll/salary-revisions/{id}/approve` | Approve a revision, close the previous active interval and activate the new version transactionally. |
| GET | `/api/payroll/employees/{employeeId}/salary-timeline` | Return immutable salary versions and audit references. |
| POST | `/api/payroll/preview` | Calculate a non-persisted payslip from an employee, period and proposed component values. |
| GET | `/api/payroll/legal-defaults?effectiveDate=2026-07-01` | Return versioned insurance/tax parameters used by the engine. |

Every write must require an idempotency key, record actor/reason/decision number, and authorize component visibility before returning amounts. Amount fields should use application field-level encryption or Always Encrypted; never log plaintext amounts.

## Calculation pseudocode

```text
CalculateMonthlySalary(employeeId, month, year):
  periodStart = first day of month
  config = EmployeeSalaries
	.where(EmployeeId == employeeId, Status == Active,
		   EffectiveDate <= periodStart,
		   EndDate is null or EndDate >= periodStart)
	.orderByDescending(EffectiveDate).single()

  components = EmployeeSalaryDetails(config.Id)
  context = attendance, approved overtime, leave, advances, legal defaults(periodStart)
  evaluate formulas in dependency order with a whitelist:
	SYS_WORKING_DAYS, ACTUAL_WORK_DAYS, BASE_SALARY, OT_HOURS,
	INSURANCE_SALARY and MIN/MAX/IF (no arbitrary code execution)

  gross = sum(earning components)
  taxableExempt = sum(components with Exempt or capped exemption)
  taxableGross = gross - taxableExempt

  insuranceSalary = choose(actual contract salary or configured fixed base)
  insuranceSalary = clamp(insuranceSalary, legal.referenceSalary, legal.insuranceCeiling)
  employeeInsurance = insuranceSalary * 10.5% when insurance eligibility applies
  employerInsurance = insuranceSalary * 21.5%

  personalDeduction = 15,500,000
  dependentDeduction = dependentCount * 6,200,000
  taxableIncome = max(0, taxableGross - employeeInsurance
					   - personalDeduction - dependentDeduction)
  pit = progressiveTax(taxableIncome, [
	(10,000,000, 5%), (30,000,000, 10%), (60,000,000, 20%),
	(100,000,000, 30%), (infinity, 35%)])

  net = gross + taxableExempt - employeeInsurance - pit - approvedAdvances - penalties
  return gross, component breakdown, insurance, deductions, pit, net
```

For trial/contract income, apply the configured 10% withholding rule only when the one-time income reaches the configured threshold (5,000,000 VND), and keep this branch separate from the progressive payroll branch.

## UI component tree

```text
PayrollModule
├── GlobalConfigTab
│   ├── SalaryComponentsSubTab
│   │   └── SalaryComponentModal (flags, formula, tax/insurance treatment)
│   ├── SalaryTemplatesSubTab
│   │   └── SalaryTemplateEditor (components, default formulas/caps)
│   └── LegalDefaultsSubTab
└── EmployeeProfilesTab
	├── EmployeeMasterTable (department, position, salary status filters)
	└── EmployeeDetailDrawer
		├── CurrentPackageSubTab (insurance + PIT blocks)
		├── TimelineSubTab (effective-date snapshots)
		└── RevisionSubTab (old/new diff + approval submit)
```

The current Razor Page is the first implementation of this flow. Its existing Save handler remains backward-compatible while the component-based persistence is introduced through a reviewed migration/API rollout.

## Default legal parameter version

The page displays the requested defaults with `EffectiveDate = 2026-07-01`: employee insurance 10.5%, employer insurance 21.5%, reference salary 2,530,000 VND, insurance ceiling 50,600,000 VND, personal deduction 15,500,000 VND, dependent deduction 6,200,000 VND, five PIT brackets, and meal exemption cap 1,200,000 VND. These values must be confirmed against the effective legal instruments before production activation and kept versioned rather than hard-coded in the calculation service.
