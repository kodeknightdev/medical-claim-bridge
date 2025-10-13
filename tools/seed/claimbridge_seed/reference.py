"""Reference values shared with the .NET services (see ClaimBridge.Contracts.Reference.SeedIds)."""

ORGS = {
    "a1000000-0000-0000-0000-000000000001": {
        "name": "Sunrise Family Clinic", "npi": "1999000015", "rendering": ["1999000114", "1999000122"], "kind": "primary",
    },
    "a1000000-0000-0000-0000-000000000002": {
        "name": "Lakeside Orthopedics", "npi": "1999000023", "rendering": ["1999000213", "1999000221"], "kind": "ortho",
    },
    "a1000000-0000-0000-0000-000000000003": {
        "name": "Harbor Pediatrics", "npi": "1999000031", "rendering": ["1999000312", "1999000320"], "kind": "peds",
    },
}

# enrollments that are active in the providers seed; claims only go to these
ENROLLED = {
    "a1000000-0000-0000-0000-000000000001": ["PAYER-A", "PAYER-B", "PAYER-C"],
    "a1000000-0000-0000-0000-000000000002": ["PAYER-A", "PAYER-B"],
    "a1000000-0000-0000-0000-000000000003": ["PAYER-A", "PAYER-C"],
}

PAYERS = {
    "PAYER-A": {"prefix": "NWH", "plan": "Northwind Silver PPO", "filing_days": 90, "fee": 0.80},
    "PAYER-B": {"prefix": "BPK", "plan": "Bluepeak Standard HMO", "filing_days": 180, "fee": 0.75},
    "PAYER-C": {"prefix": "CCI", "plan": "Cascade Basic EPO", "filing_days": 365, "fee": 0.70},
}

# procedure code -> (category, typical charge); descriptions intentionally omitted (see scrubber code sets)
PROCEDURES = {
    "99202": ("EM", 110), "99203": ("EM", 165), "99204": ("EM", 250), "99212": ("EM", 75), "99213": ("EM", 120),
    "99214": ("EM", 175), "99215": ("EM", 245), "99385": ("Preventive", 230), "99392": ("Preventive", 180),
    "99395": ("Preventive", 210), "99396": ("Preventive", 225), "90471": ("Vaccine", 30), "90686": ("Vaccine", 40),
    "90715": ("Vaccine", 55), "36415": ("Lab", 15), "80053": ("Lab", 45), "80061": ("Lab", 50), "83036": ("Lab", 40),
    "85025": ("Lab", 35), "87880": ("Lab", 30), "93000": ("Procedure", 60), "71046": ("Imaging", 95),
    "73030": ("Imaging", 85), "73562": ("Imaging", 90), "72148": ("Imaging", 1450), "73721": ("Imaging", 1350),
    "11102": ("Procedure", 150), "20610": ("Procedure", 180), "29881": ("Procedure", 4800), "27447": ("Procedure", 24000),
    "97110": ("Therapy", 45), "97140": ("Therapy", 50), "97161": ("Therapy", 120),
}

PRIOR_AUTH_REQUIRED = {"73721", "72148", "29881", "27447"}

# encounter templates per practice type: (weight, procedures, diagnoses)
ENCOUNTERS = {
    "primary": [
        (30, ["99213"], ["I10"]), (18, ["99214", "36415", "83036"], ["E11.9", "I10"]), (10, ["99396", "80061"], ["Z00.00", "E78.5"]),
        (8, ["99213", "87880"], ["J02.9"]), (8, ["99214", "90471", "90686"], ["E11.65", "Z23"]), (6, ["99212"], ["J06.9"]),
        (5, ["99214", "93000"], ["R07.9", "I10"]), (5, ["99213", "71046"], ["R05.9", "J20.9"]), (4, ["99215"], ["F41.1", "F32.A"]),
        (3, ["99213", "11102"], ["L82.1"]), (3, ["99203", "80053"], ["R73.03"]),
    ],
    "ortho": [
        (25, ["99214", "73562"], ["M17.11", "M25.561"]), (15, ["99213", "20610", "J1100"], ["M17.12"]),
        (12, ["99204", "73721"], ["M23.221", "M25.561"]), (10, ["97110", "97140"], ["M54.50", "M62.81"]),
        (8, ["97161"], ["M25.561"]), (6, ["99213", "72148"], ["M51.16", "M54.50"]), (5, ["29881"], ["S83.241A"]),
        (3, ["27447"], ["M17.11"]), (8, ["99213", "73030"], ["M25.511", "M75.101"]), (8, ["99214"], ["M48.061"]),
    ],
    "peds": [
        (30, ["99392", "90471", "90715"], ["Z00.129", "Z23"]), (20, ["99213", "87880"], ["J02.0"]),
        (15, ["99213"], ["H66.90"]), (12, ["99212"], ["J06.9"]), (10, ["99214"], ["J45.909"]), (8, ["99213"], ["R50.9", "B34.9"]),
    ],
}
PROCEDURES["J1100"] = ("Drug", 5)

CARC_CATEGORY = {
    "197": "Authorization", "4": "Coding", "11": "Coding", "16": "MissingInformation", "29": "TimelyFiling",
    "27": "Eligibility", "50": "MedicalNecessity", "151": "Coding", "18": "Duplicate", "96": "NonCovered", "97": "Bundling",
}
