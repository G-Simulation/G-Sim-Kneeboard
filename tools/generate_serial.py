"""
Seriennummer-Generator für gsim Kneeboard Server
Format: GSIM-XXXX-XXXX-XXXX (X = Hex 0-9, A-F)

Teil 1+2: 8 zufällige Hex-Zeichen (Payload)
Teil 3: 4 Hex-Zeichen (Prüfsumme)

Prüfsumme = (Summe ASCII-Werte der 8 Payload-Zeichen * 31) mod 0xFFFF
"""

import random
import sys


def generate_serial():
    # 8 zufällige Hex-Zeichen als Payload
    payload = ''.join(random.choice('0123456789ABCDEF') for _ in range(8))

    # Prüfsumme berechnen
    ascii_sum = sum(ord(c) for c in payload)
    checksum = (ascii_sum * 31) % 0xFFFF
    check_hex = f"{checksum:04X}"

    return f"GSIM-{payload[:4]}-{payload[4:]}-{check_hex}"


def validate_serial(serial):
    serial = serial.strip().upper()
    if len(serial) != 19 or not serial.startswith("GSIM-"):
        return False

    parts = serial.split('-')
    if len(parts) != 4:
        return False

    payload = parts[1] + parts[2]
    check_part = parts[3]

    if len(payload) != 8 or len(check_part) != 4:
        return False

    valid_chars = set('0123456789ABCDEF')
    if not all(c in valid_chars for c in payload + check_part):
        return False

    ascii_sum = sum(ord(c) for c in payload)
    checksum = (ascii_sum * 31) % 0xFFFF
    expected = f"{checksum:04X}"

    return check_part == expected


if __name__ == "__main__":
    count = int(sys.argv[1]) if len(sys.argv) > 1 else 1

    print(f"Generiere {count} Seriennummer(n):\n")
    for i in range(count):
        serial = generate_serial()
        valid = validate_serial(serial)
        print(f"  {serial}  (valid: {valid})")
