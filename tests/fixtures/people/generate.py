#!/usr/bin/env python3
"""Generates the synthetic S2 import fixtures. All people, numbers and identifiers are fake.

  python3 tests/fixtures/people/generate.py

employees_200.csv         200 valid rows (needs departments FIN, OPS, HR; branch NBO; bank 01/001; see README)
employees_200_errors.csv  the same file with 12 rows broken in documented ways
"""
import csv, os, random

COLUMNS = [
    "employee_number", "hire_date", "first_name", "middle_name", "last_name", "date_of_birth", "gender", "citizenship",
    "residency_status", "national_id", "passport_no", "work_permit_no", "work_permit_expiry", "kra_pin", "nssf_no", "sha_no",
    "helb_ref", "disability_cert_no", "disability_cert_expiry", "phone_mobile", "email_work", "email_personal",
    "intern_guardian_consent", "worker_category", "pay_basis", "job_title", "contract_end_date", "probation_end_date",
    "department_code", "position_code", "branch_code", "cost_center_code", "grade_code", "work_schedule_code",
    "manager_employee_number", "payment_method", "bank_code", "bank_branch_code", "account_no", "account_name", "mpesa_phone",
    "emergency_contact_name", "emergency_contact_relationship", "emergency_contact_phone",
    "emergency_contact2_name", "emergency_contact2_relationship", "emergency_contact2_phone",
]
FIRST = ["Achieng", "Wanjiru", "Kamau", "Otieno", "Njeri", "Mutua", "Chebet", "Kiprono", "Amina", "Hassan", "Wambui", "Ochieng",
         "Nyambura", "Kibet", "Auma", "Mwangi", "Zawadi", "Baraka", "Imani", "Juma"]
LAST = ["Odhiambo", "Kariuki", "Mwangi", "Wekesa", "Njoroge", "Kiplagat", "Onyango", "Mutiso", "Ali", "Omondi", "Chege", "Rotich",
        "Wafula", "Kilonzo", "Barasa", "Nyaga", "Korir", "Owino", "Macharia", "Mohamed"]
DEPTS = ["FIN", "OPS", "HR"]


def rows():
    rnd = random.Random(20261101)
    out = []
    for i in range(1, 201):
        fn, ln = FIRST[i % 20], LAST[(i * 7) % 20]
        cat, basis, title, contract_end, probation_end, consent = "permanent", "monthly", "Officer", "", "", ""
        dob = f"{1970 + i % 30}-{1 + i % 12:02d}-{1 + i % 27:02d}"
        if i % 10 == 3:
            cat, basis, title = "casual", "daily", "General worker"
        elif i % 10 == 5:
            cat, contract_end = "fixed_term", "2027-12-31"
        elif i % 10 == 7:
            cat, probation_end = "probation", "2026-12-31"
        elif i % 25 == 9:
            cat, basis, title = "piece_rate", "piece_rate", "Picker"
        elif i % 50 == 11:
            cat, title = "contractor", "Consultant"
        non_kenyan = i % 40 == 13
        method = ["bank", "mpesa", "cash"][i % 3] if i % 3 != 2 else "bank"
        number = f"E{i:04d}"
        manager = "" if i == 1 else ("E0001" if i <= 10 else f"E{1 + i % 10:04d}")
        r = {c: "" for c in COLUMNS}
        r.update({
            "employee_number": number,
            "hire_date": f"{2015 + i % 10}-{1 + i % 12:02d}-01" if i % 2 else f"01/{1 + i % 12:02d}/{2015 + i % 10}",
            "first_name": fn, "middle_name": "" if i % 4 else "Nduta", "last_name": ln, "date_of_birth": dob,
            "gender": ["female", "male"][i % 2], "citizenship": "non_kenyan" if non_kenyan else "kenyan",
            "residency_status": "resident",
            "national_id": "" if non_kenyan else f"{20000000 + i}",
            "passport_no": f"PA{900000 + i}" if non_kenyan else "",
            "work_permit_no": f"WP{5000 + i}" if non_kenyan else "",
            "work_permit_expiry": "2028-06-30" if non_kenyan else "",
            "kra_pin": f"A{100000000 + i:09d}{chr(65 + i % 26)}",
            "nssf_no": "" if cat == "contractor" or i % 17 == 0 else f"{700000 + i}",
            "sha_no": "" if cat == "contractor" or i % 19 == 0 else f"SHA{800000 + i}",
            "phone_mobile": f"07{(12000000 + i * 37) % 100000000:08d}",
            "email_work": f"{fn.lower()}.{ln.lower()}{i}@example.co.ke",
            "intern_guardian_consent": consent,
            "worker_category": cat, "pay_basis": basis, "job_title": title,
            "contract_end_date": contract_end, "probation_end_date": probation_end,
            "department_code": DEPTS[i % 3], "branch_code": "NBO",
            "manager_employee_number": manager,
            "payment_method": method,
            "emergency_contact_name": f"{FIRST[(i + 5) % 20]} {ln}", "emergency_contact_relationship": "spouse",
            "emergency_contact_phone": f"+2547{(22000000 + i * 53) % 100000000:08d}",
        })
        if method == "bank":
            r.update({"bank_code": "01", "bank_branch_code": "001", "account_no": f"{1100000000 + i}", "account_name": f"{fn} {ln}"})
        elif method == "mpesa":
            r["mpesa_phone"] = r["phone_mobile"]
        out.append(r)
    return out


# Row number in the file = index + 2 (header is line 1). Each break names the field the import must flag.
BREAKS = {
    5: ("kra_pin", "P051234567X"),            # employer PIN given for an individual
    17: ("national_id", "20000003"),          # duplicates row of E0003
    23: ("department_code", "NOPE"),          # unknown department
    31: ("pay_basis", "monthly"),             # casual paid monthly (row 33 is casual)
    44: ("emergency_contact_phone", ""),      # no emergency contact phone
    58: ("phone_mobile", "12345"),            # not a Kenyan mobile
    66: ("date_of_birth", "2015-01-01"),      # under 18, not an intern
    80: ("work_permit_no", ""),               # non-Kenyan (E0093? see below) without a permit
    97: ("hire_date", "31/31/2020"),          # not a date
    120: ("employee_number", "E0002"),        # duplicate employee number in file
    150: ("manager_employee_number", "E9999"),  # unknown manager
    188: ("worker_category", "seasonal"),     # unknown category
}


def write(path, data):
    with open(path, "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=COLUMNS, lineterminator="\n")
        w.writeheader()
        w.writerows(data)


if __name__ == "__main__":
    here = os.path.dirname(os.path.abspath(__file__))
    good = rows()
    write(os.path.join(here, "employees_200.csv"), good)
    bad = [dict(r) for r in good]
    # Make the permit break land on a non-Kenyan row.
    non_kenyan = [k for k, r in enumerate(bad) if r["citizenship"] == "non_kenyan"]
    breaks = dict(BREAKS)
    breaks.pop(80)
    breaks[non_kenyan[1]] = ("work_permit_no", "")
    casual = [k for k, r in enumerate(bad) if r["worker_category"] == "casual"]
    breaks.pop(31)
    breaks[casual[3]] = ("pay_basis", "monthly")
    for k, (field, value) in sorted(breaks.items()):
        bad[k][field] = value
    write(os.path.join(here, "employees_200_errors.csv"), bad)
    with open(os.path.join(here, "employees_200_errors.expected.txt"), "w") as f:
        for k, (field, _) in sorted(breaks.items()):
            f.write(f"{k + 2},{field}\n")
