// Tooling VM: runs the data seeder, the load generator and SQL scripts, and is
// the admin box for SQL user setup. Public IP is for SSH only; the subnet NSG
// restricts SSH to the operator IP.

param location string
param tags object
param vmName string

@description('About 4 vCPU so the load generator is never the bottleneck.')
param vmSize string

param adminUser string

@description('OpenSSH public key. Password authentication is disabled.')
param sshPublicKey string

param subnetId string

@description('User-assigned identities to attach (ticket 07 supplies the tooling identity).')
param userAssignedIdentityIds array = []

resource publicIp 'Microsoft.Network/publicIPAddresses@2024-05-01' = {
  name: '${vmName}-pip'
  location: location
  tags: tags
  sku: {
    name: 'Standard'
  }
  properties: {
    publicIPAllocationMethod: 'Static'
  }
}

resource nic 'Microsoft.Network/networkInterfaces@2024-05-01' = {
  name: '${vmName}-nic'
  location: location
  tags: tags
  properties: {
    ipConfigurations: [
      {
        name: 'ipconfig1'
        properties: {
          privateIPAllocationMethod: 'Dynamic'
          subnet: {
            id: subnetId
          }
          publicIPAddress: {
            id: publicIp.id
          }
        }
      }
    ]
  }
}

resource vm 'Microsoft.Compute/virtualMachines@2024-07-01' = {
  name: vmName
  location: location
  tags: tags
  identity: empty(userAssignedIdentityIds)
    ? null
    : {
        type: 'UserAssigned'
        userAssignedIdentities: toObject(userAssignedIdentityIds, id => id, id => {})
      }
  properties: {
    hardwareProfile: {
      vmSize: vmSize
    }
    storageProfile: {
      imageReference: {
        publisher: 'Canonical'
        offer: 'ubuntu-24_04-lts'
        sku: 'server'
        version: 'latest'
      }
      osDisk: {
        createOption: 'FromImage'
        deleteOption: 'Delete'
        managedDisk: {
          storageAccountType: 'Premium_LRS'
        }
      }
    }
    osProfile: {
      computerName: take(vmName, 15)
      adminUsername: adminUser
      customData: loadFileAsBase64('cloud-init.yaml')
      linuxConfiguration: {
        disablePasswordAuthentication: true
        ssh: {
          publicKeys: [
            {
              path: '/home/${adminUser}/.ssh/authorized_keys'
              keyData: sshPublicKey
            }
          ]
        }
      }
    }
    networkProfile: {
      networkInterfaces: [
        {
          id: nic.id
          properties: {
            deleteOption: 'Delete'
          }
        }
      ]
    }
  }
}

output vmName string = vm.name
output publicIpAddress string = publicIp.properties.ipAddress
