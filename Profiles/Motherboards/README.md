# Motherboard profiles

Every board gets an independent profile. Do not infer BIOS compatibility from the chipset alone.

## Profile requirements
- Manufacturer and product strings as exposed by SMBIOS.
- Official product and revision-specific support URLs.
- Chipset, socket, memory topology and integrated controllers supported by official documentation.
- Vendor utilities and their side effects (voltage, clock, fan, firmware, services).
- A conservative configuration policy and explicit blocked actions.
- Manual confirmation of the physical PCB revision before firmware operations.

## First profile
- [GIGABYTE A520M K V2](GIGABYTE/A520M-K-V2.json)
- [Safe configuration policy](../../Configs/Motherboards/GIGABYTE/A520M-K-V2-safe.json)

The SMBIOS board version is not guaranteed to equal the printed PCB revision. Use generic support links if the revision is uncertain.
