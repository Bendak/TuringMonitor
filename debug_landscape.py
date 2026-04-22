import sys
import os

# Adiciona o diretório do repo clonado ao path
sys.path.append('/home/mauricio/turing-smart-screen-python')

from library.lcd.lcd_comm_rev_a import LcdCommRevA, Orientation
from PIL import Image

PORT = "/dev/ttyACM0"

def debug_landscape():
    try:
        print(f"Debug Landscape (480x320) em {PORT}...")
        lcd = LcdCommRevA(com_port=PORT, display_width=320, display_height=480)
        
        # 1. Forçar Orientação LANDSCAPE (2)
        print("  Configurando Orientação LANDSCAPE...")
        lcd.SetOrientation(Orientation.LANDSCAPE)
        
        # 2. Limpar
        print("  Limpando...")
        lcd.Clear()
        
        # 3. Enviar imagem de teste (480x320)
        path = "LcdDisplay/Assets/background.png"
        if os.path.exists(path):
            print(f"  Enviando {path}...")
            img = Image.open(path)
            lcd.DisplayPILImage(img)
            print("  Enviado com sucesso!")
        else:
            print(f"  Arquivo {path} não encontrado.")

    except Exception as e:
        print(f"Erro: {e}")

if __name__ == "__main__":
    debug_landscape()
