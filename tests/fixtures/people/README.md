# Employee import fixtures (synthetic)

All names, ID numbers, KRA PINs, phone numbers and accounts are made up. Regenerate with:

```bash
python3 tests/fixtures/people/generate.py
```

| File | Purpose |
|---|---|
| `employees_200.csv` | 200 valid employees across all worker categories, incl. non-citizens with permits, in-file managers, bank and M-Pesa payment details, some without NSSF/SHA (reported as gaps). |
| `employees_200_errors.csv` | Same file with 12 rows broken on purpose. |
| `employees_200_errors.expected.txt` | `row,column` pairs the import must flag (row = line number in the file, header is line 1). |

The tenant needs departments `FIN`, `OPS`, `HR`, branch `NBO` and bank branch `01`/`001`
before these files validate. `EmployeeImportTests` sets these up and runs the S2 acceptance check.
