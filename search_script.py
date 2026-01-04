
filename = r"d:\KneeboardServer\Kneeboard Server\bin\x64\Debug\data\map.js"
try:
    with open(filename, 'r', encoding='utf-8') as f:
        lines = f.readlines()
        for i, line in enumerate(lines):
            if "function drawLines" in line:
                print(f"Found at line {i+1}")
                # Print context
                for j in range(max(0, i), min(len(lines), i + 100)):
                    print(f"{j+1}: {lines[j].rstrip()}")
                break
except Exception as e:
    print(f"Error: {e}")
