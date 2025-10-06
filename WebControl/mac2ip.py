# You may need to run this as administrator
import os
import re

def get_ip_from_mac(target_mac):
    # Run arp -a to get the ARP table
    arp_output = os.popen('arp -a').read()
    # Normalize MAC address
    target_mac = target_mac.lower().replace('-', ':')
    for line in arp_output.splitlines():
        match = re.search(r'(\d+\.\d+\.\d+\.\d+)\s+([-\w:]+)', line)
        if match:
            ip, mac = match.groups()
            mac = mac.lower().replace('-', ':')
            if mac == target_mac:
                return ip
    return None

def list_arp_table():
    # Run arp -a to get the ARP table
    arp_output = os.popen('arp -a').read()
    devices = []
    for line in arp_output.splitlines():
        match = re.search(r'(\d+\.\d+\.\d+\.\d+)\s+([-\w:]+)', line)
        if match:
            ip, mac = match.groups()
            mac = mac.lower().replace('-', ':')
            devices.append((ip, mac))
    return devices

# Example usage:
for ip, mac in list_arp_table():
    print(f"IP: {ip}  MAC: {mac}")

print(get_ip_from_mac('00:11:22:33:44:55'))  
