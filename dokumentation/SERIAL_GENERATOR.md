# Seriennummer-Generator

## Voraussetzung
Python 3 installiert.

## Seriennummer generieren

```bash
# Eine Seriennummer generieren
python generate_serial.py

# Mehrere Seriennummern generieren
python generate_serial.py 10
```

## Format
```
GSIM-XXXX-XXXX-XXXX
      |         |
      Payload   Prüfsumme
      (8 Hex)   (4 Hex)
```

## Beispiel-Ausgabe
```
GSIM-8659-ED61-37B4  (valid: True)
GSIM-D482-1F57-36DB  (valid: True)
```

## Verwendung
Seriennummer im Kneeboard Server unter **Einstellungen > Seriennummer** eingeben. Bei gültiger Nummer wird das Spendenpanel dauerhaft deaktiviert.
