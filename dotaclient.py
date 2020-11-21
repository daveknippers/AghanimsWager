import logging

import time

logging.basicConfig(format='[%(asctime)s] %(levelname)s %(name)s: %(message)s', level=logging.DEBUG)

from steam.client import SteamClient
from steam.enums import EPersonaState
from steam.enums.emsg import EMsg
from steam.utils.proto import proto_to_dict
from dota2.client import Dota2Client
from dota2.proto_enums import EDOTAGCMsg


client = SteamClient()
dota = Dota2Client(client)

steam_id = 76561199107038141

checked_servers = set()

#'''
#	for friend in msg.body.friends:
#		if friend.gameid == 570:
#			print('detected proper game in ClientPersonaState')
#			data = dict([('steam_id',friend.friendid),
#						 ('live',False)])
#			print('sending',data)
#			dota.send(EDOTAGCMsg.EMsgGCSpectateFriendGame,data)
#	'''

@client.on(EMsg.ClientPersonaState)
def investigate_ClientPersonaState(msg):
	if client.friends.ready:
		for f in client.friends:
			if f.state != EPersonaState.Offline:
				try:
					lobby_id = int(f.rich_presence['WatchableGameID'])
					data = dict([('lobby_ids',[lobby_id])])
				except KeyError:
					print(f.name,' is not in a watchable game')
					lobby_id = None
				if lobby_id != None:
					if lobby_id not in checked_servers:
						dota.send(EDOTAGCMsg.EMsgClientToGCFindTopSourceTVGames,data)
						checked_servers.add(lobby_id)
	
@dota.on(EDOTAGCMsg.EMsgGCSpectateFriendGameResponse)
def callback_SpectateFriendGame(msg):
	pass
	'''
	global DO_ONCE
	print('spectating',msg.server_steamid)
	if DO_ONCE: 
		data = dict([('lobby_ids',[msg.server_steamid])])
		dota.send(EDOTAGCMsg.EMsgClientToGCFindTopSourceTVGames,data)
	DO_ONCE = False
	'''
	
		
@dota.on(EDOTAGCMsg.EMsgGCToClientFindTopSourceTVGamesResponse)
def callbackFindTopSourceTVGames(msg):
	print(msg)

@client.friends.on('ready')
def check_friends():
	for f in client.friends:
		print(f.name,f.steam_id,f.state)


@client.on('logged_on')
def start_dota():
	dota.launch()

@dota.on('ready')
def do_dota_stuff():
	pass

client.cli_login(username='KaliRoulette',password='your_password')
client.run_forever()



