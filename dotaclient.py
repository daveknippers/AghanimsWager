import logging

#logging.basicConfig(format='[%(asctime)s] %(levelname)s %(name)s: %(message)s', level=logging.DEBUG)

from steam.client import SteamClient
from steam.enums.emsg import EMsg
from steam.utils.proto import proto_to_dict
from dota2.client import Dota2Client
from dota2.proto_enums import EDOTAGCMsg

client = SteamClient()
dota = Dota2Client(client)

steam_id = 76561199107038141

DO_ONCE = True

@client.on(EMsg.ClientPersonaState)
def investigate_ClientPersonaState(msg):
	for friend in msg.body.friends:
		if friend.gameid == 570:
			print('detected proper game in ClientPersonaState')
			data = dict([('steam_id',friend.friendid),
						 ('live',False)])
			print('sending',data)
			dota.send(EDOTAGCMsg.EMsgGCSpectateFriendGame,data)
	
@dota.on(EDOTAGCMsg.EMsgGCSpectateFriendGameResponse)
def callback_SpectateFriendGame(msg):
	global DO_ONCE
	print('spectating',msg.server_steamid)
	if DO_ONCE: 
		data = dict([('lobby_ids',[msg.server_steamid])])
		dota.send(EDOTAGCMsg.EMsgClientToGCFindTopSourceTVGames,data)
	DO_ONCE = False
	
		
@dota.on(EDOTAGCMsg.EMsgGCToClientFindTopSourceTVGamesResponse)
def callbackFindTopSourceTVGames(msg):
	print(msg)




@client.on('logged_on')
def start_dota():
    dota.launch()

@dota.on('ready')
def do_dota_stuff():
	pass

client.cli_login(username='KaliRoulette',password='testACCOUNTpleaseIGNORE')
client.run_forever()



